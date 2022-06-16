green=$'\e[0;32m'
white=$'\e[0m'
echo "${green}Welcome to the setup-test script"'!'"${white}"
echo "${green}Setting up SSH client ...${white}"

apt update
apt install openssh-client -y

echo "${green}Setting up SSH client ... Done${white}"

trap : TERM INT
sleep infinity & wait
