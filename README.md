# Nexus Remote Data Source

[![GitHub Actions](https://github.com/nexus-main/nexus-sources-remote/actions/workflows/build-and-publish.yml/badge.svg?branch=master)](https://github.com/nexus-main/nexus-sources-remote/actions)

The data source `Nexus.Sources.Remote` allows to communicate with remote systems via TCP. The remote site must listen on port `56145` for incoming connections. Two TCP connections are required: The first one is for the communication which follows the `JSON-RPC` protocol. The second one is for bi-directional data transfer. Two packages exist to simplify implemention on the remote site: `Nexus.Remoting` (C#) and `nexus-remoting` (python). These packages provide the `RemoteCommunicator` type which handles the communication for you.

The basic aim of this extension is to enable Nexus to support extensions that are written in languages other than C#. In addition, the extraction of data from files should take place as close as possible to the actual storage location in order to avoid high latencies due to random file accesses. This brings us to the next topic: `Nexus Agent`

# Nexus Agent

Nexus Agent is an application that depends on the `Nexus.Remoting` package to listen for incoming connection requests from Nexus. It can be described as a mini-Nexus, since it acts - like Nexus - as a host for extensions. It can be used to provide data to Nexus that resides on a different server. Without Nexus Agent, it would be necessary to access raw data files over the network which is often quite slow due to many high latency random file accesses. Nexus Agent helps to greatly reduce this number by doing the actual work on behalf of Nexus and returning data streams with high throughput. It is available as Docker container to enable a quick start.