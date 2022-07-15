green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

echo "${green}Welcome to the satellite.sh script!${white}"
project=$2

if [ -f "../commit_changed" ]; then
    echo "Build project ${green}${project}${white}"
    dotnet build ${project}
fi

# run user code
shift
shift
echo "Run command ${green}dotnet run --no-build --project ${project} -- $@${white}"
dotnet run --no-build --project ${project} -- $@
