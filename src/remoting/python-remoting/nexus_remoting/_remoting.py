import dataclasses
import json
import re
import socket
import struct
import typing
from datetime import datetime, timedelta
from enum import Enum
from json import JSONEncoder
from threading import Lock
from typing import Any, Dict, Type, TypeVar, Union, cast
from urllib.parse import urlparse
from uuid import UUID

from nexus_extensibility import (CatalogItem, DataSourceContext,
                                 ExtensibilityUtilities, IDataSource, ILogger,
                                 LogLevel, ReadRequest)

T = TypeVar("T")
snake_case_pattern = re.compile('((?<=[a-z0-9])[A-Z]|(?!^)[A-Z](?=[a-z]))')
timespan_pattern = re.compile('^(?:([0-9]+)\\.)?([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\\.([0-9]+))?$')

class _MyEncoder(JSONEncoder):

    def default(self, o: Any):
        return self._convert(o)

    def _convert(self, value: Any) -> Any:

        result: Any

        # date/time
        if isinstance(value, datetime):
            result = value.isoformat()

        # timedelta
        elif isinstance(value, timedelta):
            hours, remainder = divmod(value.seconds, 3600)
            minutes, seconds = divmod(remainder, 60)
            result = f"{int(value.days)}.{int(hours):02}:{int(minutes):02}:{int(seconds):02}.{value.microseconds}"

        # enum
        elif isinstance(value, Enum):
            result = value.value

        # dataclass
        elif dataclasses.is_dataclass(value):
            result = {}

            for (key, local_value) in value.__dict__.items():
                result[self._to_camel_case(key)] = self._convert(local_value)

        # normal class
        elif hasattr(value, "__dict__"):
            result = {}
            method_names = [attribute for attribute in dir(value) if not attribute.startswith("_")]

            for method_name in method_names:
                local_value = getattr(value, method_name)
                
                if not callable(local_value):
                    result[self._to_camel_case(method_name)] = self._convert(local_value)

        # else
        else:
            result = value

        return result

    def _to_camel_case(self, value: str) -> str:
        components = value.split("_")
        return components[0] + ''.join(x.title() for x in components[1:])

def _decode(cls: Type[T], data: Any) -> T:

    if data is None:
        return typing.cast(T, None)

    origin = typing.cast(Type, typing.get_origin(cls))
    args = typing.get_args(cls)

    if origin is not None:

        # Optional
        if origin is Union and type(None) in args:

            baseType = args[0]
            instance3 = _decode(baseType, data)

            return typing.cast(T, instance3)

        # list
        elif issubclass(origin, list):

            listType = args[0]
            instance1: list = list()

            for value in data:
                instance1.append(_decode(listType, value))

            return typing.cast(T, instance1)
        
        # dict
        elif issubclass(origin, dict):

            keyType = args[0]
            valueType = args[1]

            instance2: dict = dict()

            for key, value in data.items():
                key = snake_case_pattern.sub(r'_\1', key).lower()
                instance2[_decode(keyType, key)] = _decode(valueType, value)

            return typing.cast(T, instance2)

        # default
        else:
            raise Exception(f"Type {str(origin)} cannot be deserialized.")

    # datetime
    elif issubclass(cls, datetime):
        return typing.cast(T, datetime.strptime(data[:-1], "%Y-%m-%dT%H:%M:%S.%f"))

    # timedelta
    elif issubclass(cls, timedelta):
        # ^(?:([0-9]+)\.)?([0-9]Nexus-Configuration):([0-9]Nexus-Configuration):([0-9]Nexus-Configuration)(?:\.([0-9]+))?$
        # 12:08:07
        # 12:08:07.1250000
        # 3000.00:08:07
        # 3000.00:08:07.1250000
        match = timespan_pattern.match(data)

        if match:
            days = int(match.group(1)) if match.group(1) else 0
            hours = int(match.group(2)) if match.group(2) else 0
            minutes = int(match.group(3)) if match.group(3) else 0
            seconds = int(match.group(4)) if match.group(4) else 0
            milliseconds = int(match.group(5)) if match.group(5) else 0

            return typing.cast(T, timedelta(days=days, hours=hours, minutes=minutes, seconds=seconds, milliseconds=milliseconds))

        else:
            raise Exception(f"Unable to deserialize {data} into value of type timedelta.")

    # UUID
    elif issubclass(cls, UUID):
        return typing.cast(T, UUID(data))
       
    # dataclass
    elif dataclasses.is_dataclass(cls):

        p = []

        for name, value in data.items():

            type_hints = typing.get_type_hints(cls)
            name = snake_case_pattern.sub(r'_\1', name).lower()
            parameterType = typing.cast(Type, type_hints.get(name))
            value = _decode(parameterType, value)

            p.append(value)

        parameters_count = len(p)

        if (parameters_count == 0): return cls()
        if (parameters_count == 1): return cls(p[0])
        if (parameters_count == 2): return cls(p[0], p[1])
        if (parameters_count == 3): return cls(p[0], p[1], p[2])
        if (parameters_count == 4): return cls(p[0], p[1], p[2], p[3])
        if (parameters_count == 5): return cls(p[0], p[1], p[2], p[3], p[4])
        if (parameters_count == 6): return cls(p[0], p[1], p[2], p[3], p[4], p[5])
        if (parameters_count == 7): return cls(p[0], p[1], p[2], p[3], p[4], p[5], p[6])
        if (parameters_count == 8): return cls(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7])
        if (parameters_count == 9): return cls(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8])
        if (parameters_count == 10): return cls(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9])

        raise Exception("Dataclasses with more than 10 parameters cannot be deserialized.")

    # normal class
    elif hasattr(cls, "__dict__"):
        raise Exception("Not yet implemented.")

    # default
    else:
        return data

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
        
        jsonResponse = json.dumps(notification, cls=_MyEncoder, ensure_ascii = False)
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

            remove me!!!
            # with open(r"C:\Users\wilvin\Downloads\b\log.txt", "w") as file:
            #     file.write(f"{str(request)}\n")

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
            json_response = json.dumps(response, cls=_MyEncoder, ensure_ascii = False)              
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

            catalog_item = _decode(CatalogItem, params[3])
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
