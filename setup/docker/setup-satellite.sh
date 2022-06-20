set -e

green=$'\e[0;32m'
white=$'\e[0m'
echo "${green}Welcome to the setup-${satellite_id}.sh script"'!'"${white}"

echo "${green}Set up SSH server${white}"
apt update
apt install openssh-server -y
sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/g' '/etc/ssh/sshd_config'
service ssh start

echo "${green}Load run-${satellite_id}.sh and run-user.sh scripts${white}"
curl -s -O 'https://raw.githubusercontent.com/Nexusforge/nexus-sources-remote/main/setup/docker/run.sh'
curl -s -O 'https://raw.githubusercontent.com/Nexusforge/nexus-sources-remote/main/setup/docker/run-user.sh'
curl -s -O "https://raw.githubusercontent.com/Nexusforge/nexus-sources-remote/main/setup/docker/${satellite_id}/satellite.sh"

mkdir -p /var/lib/nexus
touch "/var/lib/nexus/ready"

trap : TERM INT
sleep infinity & wait
