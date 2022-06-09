import asyncio
import glob
import os
import sys
from datetime import datetime, timedelta, timezone
from typing import Callable
from urllib.request import url2pathname

from nexus_extensibility import (CatalogRegistration, DataSourceContext,
                                 IDataSource, LogLevel, NexusDataType,
                                 ReadDataHandler, ReadRequest, Representation,
                                 ResourceBuilder, ResourceCatalogBuilder)
from nexus_remoting import RemoteCommunicator


class PythonDataSource(IDataSource):
    
    async def set_context_async(self, context):
        
        self._context: DataSourceContext = context

        if (context.resource_locator.scheme != "file"):
            raise Exception(f"Expected 'file' URI scheme, but got '{context.resource_locator.scheme}'.")

        context.logger.log(LogLevel.Information, "Logging works!")

    async def get_catalog_registrations_async(self, path: str):

        if path == "/":
            return [
                CatalogRegistration("/A/B/C", "Test catalog /A/B/C."),
                CatalogRegistration("/D/E/F", "Test catalog /D/E/F.")
            ]

        else:
            return []

    async def get_catalog_async(self, catalog_id: str):

        if (catalog_id == "/A/B/C"):

            representation = Representation(NexusDataType.INT64, timedelta(seconds=1))

            resource1 = ResourceBuilder("resource1") \
                .WithUnit("°C") \
                .WithGroups(["group1"]) \
                .AddRepresentation(representation) \
                .Build()

            representation = Representation(NexusDataType.FLOAT64, timedelta(seconds=1))

            resource2 = ResourceBuilder("resource2") \
                .WithUnit("bar") \
                .WithGroups(["group2"]) \
                .AddRepresentation(representation) \
                .Build()

            catalog = ResourceCatalogBuilder("/A/B/C") \
                .WithProperty("a", "b") \
                .AddResources([resource1, resource2]) \
                .Build()

        elif (catalog_id == "/D/E/F"):

            representation = Representation(NexusDataType.FLOAT32, timedelta(seconds=1))

            resource = ResourceBuilder("resource1") \
                .WithUnit("m/s") \
                .WithGroups(["group1"]) \
                .AddRepresentation(representation) \
                .Build()

            catalog = ResourceCatalogBuilder("/D/E/F") \
                .AddResource(resource) \
                .Build()

        else:
            raise Exception("Unknown catalog ID.")

        return catalog

    async def get_time_range_async(self, catalog_id: str):

        if catalog_id != "/A/B/C":
            raise Exception("Unknown catalog ID.")

        file_paths = glob.glob(url2pathname(self._context.resource_locator.path) + "/**/*.dat", recursive=True)
        file_names = [os.path.basename(file_path) for file_path in file_paths]
        date_times = sorted([datetime.strptime(fileName, '%Y-%m-%d_%H-%M-%S.dat') for fileName in file_names])
        begin = date_times[0].replace(tzinfo = timezone.utc)
        end = date_times[-1].replace(tzinfo = timezone.utc)

        return (begin, end)

    async def get_availability_async(self, catalog_id: str, begin: datetime, end: datetime):

        if catalog_id != "/A/B/C":
            raise Exception("Unknown catalog ID.")

        period_per_file = timedelta(minutes = 10)
        max_file_count = (end - begin).total_seconds() / period_per_file.total_seconds()
        file_paths = glob.glob(url2pathname(self._context.resource_locator.path) + "/**/*.dat", recursive=True)
        file_names = [os.path.basename(file_path) for file_path in file_paths]
        date_times = [datetime.strptime(fileName, '%Y-%m-%d_%H-%M-%S.dat') for fileName in file_names]
        filtered_date_times = [current for current in date_times if current >= begin and current < end]
        actual_file_count = len(filtered_date_times)

        return actual_file_count / max_file_count

    async def read_async(self, 
        begin: datetime, 
        end: datetime,
        requests: list[ReadRequest], 
        read_data: ReadDataHandler,
        report_progress: Callable[[float], None]):

        # ############################################################################
        # Warning! This is a simplified implementation and not generally applicable! #
        # ############################################################################

        # The test files are located in a folder hierarchy where each has a length of 
        # 10-minutes (600 s):
        # ...
        # TESTDATA/2020-01/2020-01-02/2020-01-02_00-00-00.dat
        # TESTDATA/2020-01/2020-01-02/2020-01-02_00-10-00.dat
        # ...
        # The data itself is made up of progressing timestamps (unix time represented 
        # stored as 8 byte little-endian integers) with a sample rate of 1 Hz.

        for request in requests:
        
            samples_per_second = int(1 / request.catalog_item.representation.sample_period.total_seconds())
            seconds_per_file = 600
            type_size = 8

            # go
            current_begin = begin

            while current_begin < end:

                # find files
                search_pattern = url2pathname(self._context.resource_locator.path) + \
                    f"/{current_begin.strftime('%Y-%m')}/{current_begin.strftime('%Y-%m-%d')}/*.dat"

                file_paths = glob.glob(search_pattern, recursive=True)

                # for each file found in target folder
                for file_path in file_paths:

                    fileName = os.path.basename(file_path)
                    file_begin = datetime.strptime(fileName, '%Y-%m-%d_%H-%M-%S.dat')
                    
                    # if file date/time is within the limits
                    if file_begin >= current_begin and file_begin < end:

                        # compute target offset and file length
                        target_offset = int((file_begin - begin).total_seconds()) * samples_per_second
                        file_length = samples_per_second * seconds_per_file

                        # load and copy binary data
                        with open(file_path, "rb") as file:
                            file_data = file.read()

                        for i in range(0, file_length * type_size):    
                            request.data[(target_offset * type_size) + i] = file_data[i]

                        # set status to 1 for all written data
                        for i in range(0, file_length):    
                            request.status[target_offset + i] = 1

                current_begin += timedelta(days = 1)

# get address
address = "localhost"

# get port
try:
    port = int(sys.argv[1])
except Exception as ex:
    raise Exception(f"The second command line argument must be a valid port number. Inner error: {str(ex)}")

# run
asyncio.run(RemoteCommunicator(PythonDataSource(), address, port).run())
