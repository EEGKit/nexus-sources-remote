green=$'\e[0;32m'
white=$'\e[0m'
echo "${green}Welcome to the setup-main script"'!'"${white}"

echo "${green}Set up SSH client${white}"
apt update
apt install openssh-client -y

echo "Clone repository ${green}https://github.com/Nexusforge/nexus-sources-remote${white}"
git clone https://github.com/Nexusforge/nexus-sources-remote
cd nexus-sources-remote

trap : TERM INT
sleep infinity & wait
