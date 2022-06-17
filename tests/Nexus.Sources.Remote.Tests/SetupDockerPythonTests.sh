setup_folder="setup/docker"
satellite_id="python"

bash "${setup_folder}/setup-host.sh" $satellite_id
docker-compose --file "${setup_folder}/docker-compose.yml" --env-file "${setup_folder}/${satellite_id}/.env" up -d

while true; do
   docker exec "nexus-main" test -f "/var/lib/nexus/ready" && \
   docker exec "nexus-${satellite_id}" test -f "/var/lib/nexus/ready" && \
   break

   echo "Waiting for Docker containers to become ready ..."
   sleep 1;
done

docker exec nexus-main bash -c "cd /root/nexus-sources-remote; dotnet test --filter Category=docker