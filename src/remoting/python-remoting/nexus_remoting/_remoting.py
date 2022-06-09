import enum
import json
import socket
import struct
from datetime import datetime, timedelta
from threading import Lock
from typing import Any, Dict, cast
from urllib.parse import urlparse

from nexus_extensibility import DataSourceContext, IDataSource, ReadRequest


class RemoteCommunicator:
    """The remote communicator."""

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
                        (result, data, status) = await self._processInvocationAsync(request)

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
            json_response = json.dumps(response, default=lambda x: self._serializeJson(x), ensure_ascii = False)
            encoded_response = json_response.encode()

            with self._lock:
                self._tcp_comm_socket.sendall(struct.pack(">I", len(encoded_response)))
                self._tcp_comm_socket.sendall(encoded_response)

            # send data
            if data is not None and status is not None:
                self._tcp_data_socket.sendall(data)
                self._tcp_data_socket.sendall(status)

    async def _processInvocationAsync(self, request: dict[str, Any]):
        
        result = None
        data = None
        status = None

        method_name = request["method"]
        params = cast(list[Any], request["params"])

        if method_name == "getApiVersionAsync":

            result = {
                "ApiVersion": 1
            }

        elif method_name == "setContextAsync":

            resource_locator = urlparse(cast(str, params[0]))
            system_configuration = cast(Dict[str, str], params[1])
            source_configuration = cast(Dict[str, str], params[2])
            request_configuration = cast(Dict[str, str], params[3])
            logger = Logger(self._tcp_comm_socket, self._lock)

            context = DataSourceContext(
                resource_locator, 
                system_configuration, 
                source_configuration, 
                request_configuration, 
                logger)

            await self._data_source.set_context_async(context)

        elif method_name == "getCatalogRegistrationsAsync":

            path = cast(str, params[0])
            registrations = await self._data_source.get_catalog_registrations_async(path)

            result = {
                "Registrations": registrations
            }

        elif method_name == "getCatalogAsync":

            catalog_id = params[0]
            catalog = await self._data_source.get_catalog_async(catalog_id)
            
            result = {
                "Catalog": catalog
            }

        elif method_name == "getTimeRangeAsync":

            catalog_id = params[0]
            (begin, end) = await self._data_source.get_time_range_async(catalog_id)

            result = {
                "Begin": begin,
                "End": end,
            }

        elif method_name == "getAvailabilityAsync":

            catalog_id = params[0]
            begin = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[2], "%Y-%m-%dT%H:%M:%SZ")
            availability = await self._data_source.get_availability_async(catalog_id, begin, end)

            result = {
                "Availability": availability
            }

        elif method_name == "readSingleAsync":

            begin = datetime.strptime(params[0], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            catalog_item = params[3]
            read_request = ReadRequest(catalog_item, data, status)

            await self._data_source.read_async(begin, end, [read_request], cast(Any, None), cast(Any, None))

        # Add cancellation support?
        # https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/sendrequest.md#cancellation
        # https://github.com/Microsoft/language-server-protocol/blob/main/versions/protocol-2-x.md#cancelRequest
        elif method_name == "$/cancelRequest":
            pass

        else:
            raise Exception(f"Unknown method '{method_name}'.")

        return (result, data, status)

    def _shutdown(self):
        exit()

    def _serializeJson(self, x):

        if isinstance(x, enum.Enum):
            return x._name_

        if isinstance(x, timedelta):
            return str(x)

        if isinstance(x, datetime):
            return x.isoformat()

        else:
            return {key.lstrip('_'): value for key, value in vars(x).items()}
