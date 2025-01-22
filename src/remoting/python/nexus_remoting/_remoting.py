import json
import socket
import struct
import time
from datetime import datetime, timedelta
from threading import Lock
from typing import Any, Awaitable, Callable, Dict, Optional, Tuple, cast
from urllib.parse import urlparse

from nexus_extensibility import (CatalogItem, DataSourceContext,
                                 ExtensibilityUtilities, IDataSource, ILogger,
                                 LogLevel, ReadRequest, ResourceCatalog)

from ._encoder import (JsonEncoder, JsonEncoderOptions, to_camel_case,
                       to_snake_case)

_json_encoder_options: JsonEncoderOptions = JsonEncoderOptions(
    property_name_encoder=to_camel_case,
    property_name_decoder=to_snake_case
)

_lock: Lock = Lock()

class _Logger(ILogger):

    _comm_socket: socket.socket

    def __init__(self, tcp_comm_socket: socket.socket):
        self._comm_socket = tcp_comm_socket

    def log(self, log_level: LogLevel, message: str):

        notification = {
            "jsonrpc": "2.0",
            "method": "log",
            "params": [log_level.name, message]
        }
        
        _send_to_server(notification, self._comm_socket)

class RemoteCommunicator:
    """A remote communicator."""

    _watchdog_timer = time.time()
    _logger: ILogger
    _data_source: IDataSource

    def __init__(
        self, 
        comm_socket: socket.socket, 
        data_socket: socket.socket, 
        get_data_source: Callable[[str], IDataSource]
    ):
        """
        Initializes a new instance of the RemoteCommunicator.
        
            Args:
                comm_stream: The network stream for communications.
                data_stream: The network stream for data.
                get_data_source: A func to get a new data source instance by its type name.
        """

        self._comm_socket = comm_socket
        self._data_socket = data_socket
        self._get_data_source = get_data_source

    @property
    def last_communication(self) -> timedelta:
        now = time.time()
        return timedelta(seconds=now - self._watchdog_timer)

    async def run(self) -> Awaitable:
        """
        Starts the remoting operation.
        """

        # loop
        while (True):

            # https://www.jsonrpc.org/specification

            # get request message
            size = self._read_size(self._comm_socket)
            json_request = self._comm_socket.recv(size, socket.MSG_WAITALL)

            if len(json_request) == 0:
                raise Exception("The connection has been closed.")

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
            _send_to_server(response, self._comm_socket)

            # send data
            if data is not None and status is not None:
                self._data_socket.sendall(data)
                self._data_socket.sendall(status)

    async def _process_invocation(self, request: dict[str, Any]) \
        -> Tuple[
            Optional[Dict[str, Any]], 
            Optional[memoryview], 
            Optional[memoryview]
        ]:
        
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

            # TODO: make use of the type (see C# implementation)
            type = params[0]
            raw_context = params[1]
            resource_locator_string = cast(str, raw_context["resourceLocator"]) if "resourceLocator" in raw_context else None
            resource_locator = None if resource_locator_string is None else urlparse(resource_locator_string)

            self._data_source = self._get_data_source(type)

            system_configuration = raw_context["systemConfiguration"] \
                if "systemConfiguration" in raw_context else None

            source_configuration = raw_context["sourceConfiguration"] \
                if "sourceConfiguration" in raw_context else None

            request_configuration = raw_context["requestConfiguration"] \
                if "requestConfiguration" in raw_context else None

            self._logger = _Logger(self._comm_socket)

            context = DataSourceContext(
                resource_locator,
                system_configuration,
                source_configuration,
                request_configuration)

            await self._data_source.set_context(context, self._logger)

        elif method_name == "getCatalogRegistrations":

            if self._data_source is None:
                raise Exception("The data source context must be set before invoking other methods.")

            path = cast(str, params[0])
            registrations = await self._data_source.get_catalog_registrations(path)

            result = {
                "registrations": registrations
            }

        elif method_name == "enrichCatalog":

            if self._data_source is None:
                raise Exception("The data source context must be set before invoking other methods.")

            original_catalog = JsonEncoder().decode(ResourceCatalog, params[0])
            catalog = await self._data_source.enrich_catalog(original_catalog)
            
            result = {
                "catalog": catalog
            }

        elif method_name == "getTimeRange":

            if self._data_source is None:
                raise Exception("The data source context must be set before invoking other methods.")

            catalog_id = params[0]
            (begin, end) = await self._data_source.get_time_range(catalog_id)

            result = {
                "begin": begin,
                "end": end,
            }

        elif method_name == "getAvailability":

            if self._data_source is None:
                raise Exception("The data source context must be set before invoking other methods.")

            catalog_id = params[0]
            begin = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[2], "%Y-%m-%dT%H:%M:%SZ")
            availability = await self._data_source.get_availability(catalog_id, begin, end)

            result = {
                "availability": availability
            }

        elif method_name == "readSingle":

            if self._data_source is None:
                raise Exception("The data source context must be set before invoking other methods.")

            begin = datetime.strptime(params[0], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            original_resource_name = params[2]
            catalog_item = JsonEncoder.decode(CatalogItem, params[3], _json_encoder_options)
            (data, status) = ExtensibilityUtilities.create_buffers(catalog_item.representation, begin, end)
            read_request = ReadRequest(original_resource_name, catalog_item, data, status)

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

        self._logger.log(LogLevel.Debug, f"Read resource path {resource_path} from Nexus")

        read_data_request = {
            "jsonrpc": "2.0",
            "method": "readData",
            "params": [resource_path, begin, end]
        }

        _send_to_server(read_data_request, self._comm_socket)

        size = self._read_size(self._data_socket)
        data = self._data_socket.recv(size, socket.MSG_WAITALL)

        if len(data) == 0:
            raise Exception("The connection has been closed.")

        # 'cast' is required because of https://github.com/python/cpython/issues/126012
        # see also https://github.com/nexus-main/nexus/issues/184
        return cast(memoryview, memoryview(data).cast("d"))

    def _handle_report_progress(self, progress_value: float):
        pass # not implemented

    def _read_size(self, current_socket: socket.socket) -> int:
        size_buffer = current_socket.recv(4, socket.MSG_WAITALL)

        if len(size_buffer) == 0:
            raise Exception("The connection has been closed.")

        size = struct.unpack(">I", size_buffer)[0]
        return size

def _send_to_server(message: Any, current_socket: socket.socket):
    encoded = JsonEncoder.encode(message, _json_encoder_options)
    json_response = json.dumps(encoded)
    encoded_response = json_response.encode()

    with _lock:
        current_socket.sendall(struct.pack(">I", len(encoded_response)))
        current_socket.sendall(encoded_response)
