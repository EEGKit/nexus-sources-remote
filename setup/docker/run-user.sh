green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

echo "Continue as: ${green}$(whoami)${white}"

# check if git project exists
if [[ ! -d 'repository' ]]; then
    mkdir -p 'repository'
fi

cd 'repository'

(
    if [ -d '.git' ]; then
        commit_old=$(git show --format="%H" --no-patch)
        echo "${green}Pull changes${white}"
        git fetch origin "main"
        git reset --hard "origin/main"
    else
        echo "Clone repository ${orange}$1${white}"
        git clone $1 .
    fi

    commit_new=$(git show --format="%H" --no-patch)
    echo "Current commit is ${green}${commit_new}${white}"

    if [[ "$commit_new" != "$commit_old" ]]; then
        touch "../commit_changed"
    else
        rm --force "../commit_changed"
    fi
) 100>"/tmp/run-user-$(whoami).lock"

source "../satellite.sh"
