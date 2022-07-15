setup_folder="setup/docker"
satellite_ids="python dotnet"

for satellite_id in $satellite_ids; do
   bash "${setup_folder}/setup-host.sh" $satellite_id
done 

docker-compose --file "${setup_folder}/docker-compose.yml" up -d

while true; do
   docker exec "nexus-main" test -f "/var/lib/nexus/ready" && \
   docker exec "nexus-${satellite_id}" test -f "/var/lib/nexus/ready" && \
   break

   echo "Waiting for Docker containers to become ready ..."
   sleep 1;
done

docker exec nexus-main bash -c "cd /root/nexus-sources-remote; dotnet test --filter TestCategory=docker"