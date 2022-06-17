green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

echo "Continue as: ${green}$(whoami)${white}"

# check if git project exists
if [ ! -d 'repository' ]; then
    mkdir -p 'repository'
fi

cd 'repository'

(
    if [ -d '.git' ]; then
        echo "${green}Pull changes${white}"
        git fetch origin main
        git reset --hard origin/main
    else
        echo "Clone repository ${orange}$1${white}"
        git clone $1 .
    fi

    
) 100>"/tmp/run-user-$user_id.lock"

source "satellite.sh"