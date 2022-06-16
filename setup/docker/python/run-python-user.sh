red=$'\e[0;31m'
green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

echo "Continue as: ${green}$(whoami){white}"

if [ -z "$2" ]; then
    echo "yo! empty"
fi