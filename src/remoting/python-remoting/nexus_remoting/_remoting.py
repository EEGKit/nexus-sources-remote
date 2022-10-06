import json
import socket
import struct
from datetime import datetime
from threading import Lock
from typing import Any, Dict, Optional, Tuple, cast
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

_lock: Lock = Lock()

class _Logger(ILogger):

    _tcp_comm_socket: socket.socket

    def __init__(self, tcp_comm_socket: socket.socket):
        self._tcp_comm_socket = tcp_comm_socket

    def log(self, log_level: LogLevel, message: str):

        notification = {
            "jsonrpc": "2.0",
            "method": "log",
            "params": [log_level.name, message]
        }
        
        _send_to_server(notification, self._tcp_comm_socket)

class RemoteCommunicator:
    """A remote communicator."""

    def __init__(self, data_source: IDataSource, address: str, port: int):
        """
        Initializes a new instance of the RemoteCommunicator.
        
            Args:
                data_source: The data source.
                address: The address to connect to.
                port: The port to connect to.
        """

        self._address: str = address
        self._port: int = port
        self._tcp_comm_socket: socket.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._tcp_data_socket: socket.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

        if not (0 < port and port < 65536):
            raise Exception(f"The port {port} is not a valid port number.")

        self._data_source: IDataSource = data_source

    async def run(self):
        """
        Starts the remoting operation.
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
            size = self._read_size(self._tcp_comm_socket)
            json_request = self._tcp_comm_socket.recv(size, socket.MSG_WAITALL)

            if len(json_request) == 0:
                _shutdown()

            request: Dict[str, Any] = json.loads(json_request)

            # process message
            data: Optional[object] = None
            status: Optional[memoryview] = None
            response: Optional[Dict[str, Any]]

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
            _send_to_server(response, self._tcp_comm_socket)

            # send data
            if data is not None and status is not None:
                self._tcp_data_socket.sendall(data)
                self._tcp_data_socket.sendall(status)

    async def _process_invocation(self, request: dict[str, Any]) \
        -> Tuple[Optional[Dict[str, Any]], Optional[memoryview], Optional[memoryview]]:
        
        result: Optional[Dict[str, Any]] = None
        data: Optional[memoryview] = None
        status: Optional[memoryview] = None

        method_name = request["method"]
        params = cast(list[Any], request["params"])

        if method_name == "getApiVersion":

            result = {
                "apiVersion": 1
            }

        elif method_name == "setContext":

            raw_context = params[0]
            resource_locator_string = cast(str, raw_context["resourceLocator"]) if "resourceLocator" in raw_context else None
            resource_locator = None if resource_locator_string is None else urlparse(resource_locator_string)

            system_configuration = raw_context["systemConfiguration"] \
                if "systemConfiguration" in raw_context else None

            source_configuration = raw_context["sourceConfiguration"] \
                if "sourceConfiguration" in raw_context else None

            request_configuration = raw_context["requestConfiguration"] \
                if "requestConfiguration" in raw_context else None

            logger = _Logger(self._tcp_comm_socket)

            context = DataSourceContext(
                resource_locator,
                system_configuration,
                source_configuration,
                request_configuration)

            await self._data_source.set_context(context, logger)

        elif method_name == "getCatalogRegistrations":

            path = cast(str, params[0])
            registrations = await self._data_source.get_catalog_registrations(path)

            result = {
                "registrations": registrations
            }

        elif method_name == "getCatalog":

            catalog_id = params[0]
            catalog = await self._data_source.get_catalog(catalog_id)
            
            result = {
                "catalog": catalog
            }

        elif method_name == "getTimeRange":

            catalog_id = params[0]
            (begin, end) = await self._data_source.get_time_range(catalog_id)

            result = {
                "begin": begin,
                "end": end,
            }

        elif method_name == "getAvailability":

            catalog_id = params[0]
            begin = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[2], "%Y-%m-%dT%H:%M:%SZ")
            availability = await self._data_source.get_availability(catalog_id, begin, end)

            result = {
                "availability": availability
            }

        elif method_name == "readSingle":

            begin = datetime.strptime(params[0], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            catalog_item = JsonEncoder.decode(CatalogItem, params[2], _json_encoder_options)
            (data, status) = ExtensibilityUtilities.create_buffers(catalog_item.representation, begin, end)
            read_request = ReadRequest(catalog_item, data, status)

            await self._data_source.read(
                begin, 
                end, 
                [read_request], 
                self._handle_read_data, 
                self._handle_report_progress)

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

    async def _handle_read_data(self, resource_path: str, begin: datetime, end: datetime) -> memoryview:

        read_data_request = {
            "jsonrpc": "2.0",
            "method": "readData",
            "params": [resource_path, begin, end]
        }

        _send_to_server(read_data_request, self._tcp_comm_socket)

        size = self._read_size(self._tcp_data_socket)
        data = self._tcp_data_socket.recv(size, socket.MSG_WAITALL)

        if len(data) == 0:
            _shutdown()

        return memoryview(data).cast("d")

    def _handle_report_progress(self, progress_value: float):
        pass # not implemented

    def _read_size(self, current_socket: socket.socket) -> int:
        size_buffer = current_socket.recv(4, socket.MSG_WAITALL)

        if len(size_buffer) == 0:
            _shutdown()

        size = struct.unpack(">I", size_buffer)[0]
        return size

def _send_to_server(message: Any, current_socket: socket.socket):
    encoded = JsonEncoder.encode(message, _json_encoder_options)
    json_response = json.dumps(encoded)
    encoded_response = json_response.encode()

    with _lock:
        current_socket.sendall(struct.pack(">I", len(encoded_response)))
        current_socket.sendall(encoded_response)

def _shutdown():
    exit()
