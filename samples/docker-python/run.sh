green=$'\e[0;32m'
white=$'\e[0m'
echo "${green}Welcome to the nexus-python run script"'!'"${white}"
echo "${green}Setting up SSH ...${white}"

apt update
apt install openssh-server -y
sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/g' /etc/ssh/sshd_config
service ssh start

echo "${green}Setting up SSH ... Done.${white}"

trap : TERM INT
sleep infinity & wait
