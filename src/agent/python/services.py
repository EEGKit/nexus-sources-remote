import asyncio
import socket
import time
import uuid
from datetime import timedelta
from logging import Logger
from typing import Optional

from apollo3zehn_package_management._services import (ExtensionHive,
                                                      PackageService)
from nexus_remoting._remoting import RemoteCommunicator


class TcpClientPair:
    comm: Optional[socket.socket]
    data: Optional[socket.socket]
    remote_communicator: Optional[RemoteCommunicator]
    watchdog_timer = time.time()
    task: Optional[asyncio.Task]

class AgentService:

    CLIENT_TIMEOUT = timedelta(minutes=1)

    _tcp_client_pairs: dict[uuid.UUID, TcpClientPair] = {}
    _lock = asyncio.Lock()

    def __init__(
            self, 
            extension_hive: ExtensionHive, 
            package_service: PackageService, 
            logger: Logger, 
            json_rpc_listen_address: str,
            json_rpc_listen_port: int
        ):
        
        self._extension_hive = extension_hive
        self._package_service = package_service
        self._logger = logger
        self._json_rpc_listen_address = json_rpc_listen_address
        self._json_rpc_listen_port = json_rpc_listen_port

    async def load_packages(self):

        self._logger.info("Load packages")
        package_reference_map = await self._package_service.get_all()
        await self._extension_hive.load_packages(package_reference_map)

    def accept_clients(self):

        self._logger.info(
            "Listening for JSON-RPC communication on %s:%d",
            self._json_rpc_listen_address,
            self._json_rpc_listen_port
        )

        tcp_listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        tcp_listener.bind((self._json_rpc_listen_address, self._json_rpc_listen_port))
        tcp_listener.listen()

        async def detect_and_remove_inactive_clients():

            while True:

                await asyncio.sleep(600)

                for key, pair in list(self._tcp_client_pairs.items()):

                    now = time.time()
                    watchdog_timer_elasped = timedelta(seconds=now - pair.watchdog_timer)

                    is_dead =\
                        (pair.comm is None or pair.data is None) and watchdog_timer_elasped >= self.CLIENT_TIMEOUT or \
                        pair.remote_communicator is not None and pair.remote_communicator.last_communication >= self.CLIENT_TIMEOUT

                    if is_dead:

                        if key in self._tcp_client_pairs:

                            # TODO: Add proper cancellation https://medium.com/nuculabs/cancellation-token-pattern-in-python-b549d894e244
                            # Otherwise sockets may stay open
                            if pair.task is not None:
                                pair.task.cancel()

                            del self._tcp_client_pairs[key]

        asyncio.create_task(detect_and_remove_inactive_clients())
        
        async def accept_new_clients():
        
            loop = asyncio.get_event_loop()

            while True:
                client, _ = await loop.sock_accept(tcp_listener)
                asyncio.create_task(self._handle_client(client))

        asyncio.create_task(accept_new_clients())

    async def _handle_client(self, client: socket.socket):

        stream_read_timeout = 1
        client.settimeout(stream_read_timeout)

        # Get connection id
        buffer1 = client.recv(36)
        id_string = buffer1.decode("utf-8")

        # Get connection type
        buffer2 = client.recv(4)
        type_string = buffer2.decode("utf-8")

        try:
            id = uuid.UUID(id_string)

        except:
            client.close()
            return

        self._logger.debug("Accept TCP client with connection ID %s and communication type %s", id_string, type_string)

        async with self._lock:

            # We got a "comm" tcp connection
            if type_string == "comm":

                if id not in self._tcp_client_pairs:
                    self._tcp_client_pairs[id] = TcpClientPair()

                self._tcp_client_pairs[id].comm = client

            # We got a "data" tcp connection
            elif type_string == "data":

                if id not in self._tcp_client_pairs:
                    self._tcp_client_pairs[id] = TcpClientPair()

                self._tcp_client_pairs[id].data = client
                
            # Something went wrong, close the socket and return
            else:
                client.close()
                return

            pair = self._tcp_client_pairs[id]

            if pair.comm and pair.data and not pair.remote_communicator:

                self._logger.debug("Accept remoting client with connection ID %s", id)

                pair.remote_communicator = RemoteCommunicator(
                    pair.comm,
                    pair.data,
                    get_data_source=lambda type: self._extension_hive.get_instance(type)
                )

                pair.task = asyncio.create_task(pair.remote_communicator.run())