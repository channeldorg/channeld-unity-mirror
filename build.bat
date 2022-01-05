Unity -batchmode -nographics -projectPath . -executeMethod BuildScript.BuildLinuxServer -logFile build.log -quit
docker build -t channeld/tanks .