import json
import socket
import struct
from datetime import datetime
from threading import Lock
from typing import Any, Dict, cast
from urllib.parse import urlparse

from nexus_extensibility import (CatalogItem, DataSourceContext,
                                 ExtensibilityUtilities, IDataSource, ILogger,
                                 LogLevel, ReadRequest)

from ._encoder import (JsonEncoder, JsonEncoderOptions, to_camel_case,
                       to_snake_case)

_json_encoder_options: JsonEncoderOptions = JsonEncoderOptions(
    property_name_encoder=to_camel_case,
    property_name_decoder=to_snake_case
)

class _Logger(ILogger):

    _tcpCommSocket: socket.socket
    _lock: Lock

    def __init__(self, tcp_socket: socket.socket, lock: Lock):
        self._tcpCommSocket = tcp_socket
        self._lock = lock

    def log(self, log_level: LogLevel, message: str):

        notification = {
            "jsonrpc": "2.0",
            "method": "log",
            "params": [log_level.name, message]
        }
        
        encoded = JsonEncoder.encode(notification, _json_encoder_options)
        jsonResponse = json.dumps(encoded)
        encodedResponse = jsonResponse.encode()

        with self._lock:
            self._tcpCommSocket.sendall(struct.pack(">I", len(encodedResponse)))
            self._tcpCommSocket.sendall(encodedResponse)

class RemoteCommunicator:
    """A remote communicator."""

    def __init__(self, data_source: IDataSource, address: str, port: int):
        """
        Initializes a new instance of the RemoteCommunicator
        
            Args:
                data_source: The data source.
                address: The address to connect to.
                port: The port to connect to.
        """

        self._address: str = address
        self._port: int = port
        self._lock: Lock = Lock()
        self._tcp_comm_socket: socket.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._tcp_data_socket: socket.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

        if not (0 < port and port < 65536):
            raise Exception(f"The port {port} is not a valid port number.")

        self._data_source: IDataSource = data_source

    async def run(self):
        """
        Starts the remoting operation
        """

        # comm connection
        self._tcp_comm_socket.connect((self._address, self._port))
        self._tcp_comm_socket.sendall("comm".encode())

        # data connection
        self._tcp_data_socket.connect((self._address, self._port))
        self._tcp_data_socket.sendall("data".encode())

        # loop
        while (True):

            # https://www.jsonrpc.org/specification

            # get request message
            size_buffer = self._tcp_comm_socket.recv(4, socket.MSG_WAITALL)

            if len(size_buffer) == 0:
                self._shutdown()

            size = struct.unpack(">I", size_buffer)[0]
            json_request = self._tcp_comm_socket.recv(size, socket.MSG_WAITALL)

            if len(size_buffer) == 0:
                self._shutdown()

            request: Dict[str, Any] = json.loads(json_request)

            # process message
            data = None
            status = None
            response: Dict[str, Any]

            if "jsonrpc" in request and request["jsonrpc"] == "2.0":

                if "id" in request:

                    try:
                        (result, data, status) = await self._process_invocation(request)

                        response = {
                            "result": result
                        }

                    except Exception as ex:
                        
                        response = {
                            "error": {
                                "code": -1,
                                "message": str(ex)
                            }
                        }

                else:
                    raise Exception(f"JSON-RPC 2.0 notifications are not supported.") 

            else:              
                raise Exception(f"JSON-RPC 2.0 message expected, but got something else.") 
            
            response["jsonrpc"] = "2.0"
            response["id"] = request["id"]

            # send response
            encoded = JsonEncoder.encode(response, _json_encoder_options)
            json_response = json.dumps(encoded)
            encoded_response = json_response.encode()

            with self._lock:
                self._tcp_comm_socket.sendall(struct.pack(">I", len(encoded_response)))
                self._tcp_comm_socket.sendall(encoded_response)

            # send data
            if data is not None and status is not None:
                self._tcp_data_socket.sendall(data)
                self._tcp_data_socket.sendall(status)

    async def _process_invocation(self, request: dict[str, Any]):
        
        result = None
        data = None
        status = None

        method_name = request["method"]
        params = cast(list[Any], request["params"])

        if method_name == "getApiVersionAsync":

            result = {
                "apiVersion": 1
            }

        elif method_name == "setContextAsync":

            raw_context = params[0]
            resource_locator = urlparse(cast(str, raw_context["resourceLocator"]))

            system_configuration = raw_context["systemConfiguration"] \
                if "systemConfiguration" in raw_context else None

            source_configuration = raw_context["sourceConfiguration"] \
                if "sourceConfiguration" in raw_context else None

            request_configuration = raw_context["requestConfiguration"] \
                if "requestonfiguration" in raw_context else None

            logger = _Logger(self._tcp_comm_socket, self._lock)

            context = DataSourceContext(
                resource_locator,
                system_configuration,
                source_configuration,
                request_configuration)

            await self._data_source.set_context_async(context, logger)

        elif method_name == "getCatalogRegistrationsAsync":

            path = cast(str, params[0])
            registrations = await self._data_source.get_catalog_registrations_async(path)

            result = {
                "registrations": registrations
            }

        elif method_name == "getCatalogAsync":

            catalog_id = params[0]
            catalog = await self._data_source.get_catalog_async(catalog_id)
            
            result = {
                "catalog": catalog
            }

        elif method_name == "getTimeRangeAsync":

            catalog_id = params[0]
            (begin, end) = await self._data_source.get_time_range_async(catalog_id)

            result = {
                "begin": begin,
                "end": end,
            }

        elif method_name == "getAvailabilityAsync":

            catalog_id = params[0]
            begin = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[2], "%Y-%m-%dT%H:%M:%SZ")
            availability = await self._data_source.get_availability_async(catalog_id, begin, end)

            result = {
                "availability": availability
            }

        elif method_name == "readSingleAsync":

            begin = datetime.strptime(params[0], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            catalog_item = JsonEncoder.decode(CatalogItem, params[2], _json_encoder_options)
            (data, status) = ExtensibilityUtilities.create_buffers(catalog_item.representation, begin, end)
            read_request = ReadRequest(catalog_item, data, status)

            await self._data_source.read_async(begin, end, [read_request], cast(Any, None), cast(Any, None))

        # Add cancellation support?
        # https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/sendrequest.md#cancellation
        # https://github.com/Microsoft/language-server-protocol/blob/main/versions/protocol-2-x.md#cancelRequest
        elif method_name == "$/cancelRequest":
            pass

        # Add progress support?
        # https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/progresssupport.md
        elif method_name == "$/progress":
            pass

        # Add OOB stream support?
        # https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/oob_streams.md

        else:
            raise Exception(f"Unknown method '{method_name}'.")

        return (result, data, status)

    def _shutdown(self):
        exit()
