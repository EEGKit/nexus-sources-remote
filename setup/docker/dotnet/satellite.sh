green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

echo "${green}Welcome to the satellite.sh script!${white}"
project=$1

if [ -f "../commit_changed" ]; then
    echo "Build project ${green}$1${white}"
    dotnet build ${project}
fi

# run user code
shift
echo "Run command ${green}dotnet run --no-build --project ${project} -- $@${white}"
dotnet run --no-build --project ${project} -- $@
