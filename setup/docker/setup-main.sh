set -e

green=$'\e[0;32m'
white=$'\e[0m'
echo "${green}Welcome to the setup-main.sh script"'!'"${white}"

echo "${green}Set up SSH client${white}"
apt update
apt install openssh-client -y

echo "Clone repository ${green}https://github.com/malstroem-labs/nexus-sources-remote${white}"
git clone https://github.com/malstroem-labs/nexus-sources-remote
cd nexus-sources-remote

mkdir -p /var/lib/nexus
touch "/var/lib/nexus/ready"

trap : TERM INT
sleep infinity & wait
