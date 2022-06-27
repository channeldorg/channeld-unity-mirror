protoc --csharp_out=./Assets/channeld -I %CHANNELD_PATH%\pkg\channeldpb channeld.proto
protoc --csharp_out=./Assets/channeld -I %CHANNELD_PATH%\pkg\channeldpb unity_common.proto
protoc --csharp_out=./Assets/channeld/Examples/Tanks/Scripts -I %CHANNELD_PATH% -I %CHANNELD_PATH%\examples\unity-mirror-tanks\tankspb tanks.proto
pause