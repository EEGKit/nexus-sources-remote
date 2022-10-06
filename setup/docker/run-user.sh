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
    clone_required=false

    if [ -d '.git' ]; then
        current_tag=$(git describe --tags)
        echo "Current tag is ${green}${current_tag}${white}"

        if [ "$current_tag" != "$2" ]; then
            rm --force -r .* * 2> /dev/null
            clone_required=true
        fi
    else
        clone_required=true
    fi

    if [[ "$clone_required" = true ]]; then
        echo "Clone repository ${orange}$1${white} @ ${orange}$2${white}"
        git clone -c advice.detachedHead=false --depth 1 --branch $2 $1 .
        touch "../tag_changed"
    else
        rm --force "../tag_changed"
    fi
) 100>"/tmp/run-user-$(whoami).lock"

source "../satellite.sh"
