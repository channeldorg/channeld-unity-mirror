> From the author: It's been too long since I last updated this repo. The code should work but only with the earliest version of channeld (v0.1 / v0.2). Also, it misses the updates of Mirror since mid 2022.
> 
> If you are interested in maintaining this repo, please let me know. Thanks!

# Prerequisites
1. Make sure you have either [Go](https://go.dev) or [Docker](https://www.docker.com/products/docker-desktop) installed for running channeld.
2. Check out [channeld](https://github.com/channeldorg/channeld)
3. To run channeld, either navigate to `cmd` folder and execute `go run .`, or [use Docker Compose](https://github.com/indiest/channeld#2-docker)

# How to run the tank demo
1. Create an Unity project. Copy the content of this repo into the project
2. In the Unity Editor, open the `Assets/channeld/Examples/Tanks/Scene/TankChanneld` scene. The scene uses Mirror's [Tanks](https://mirror-networking.gitbook.io/docs/examples/tanks) example and has everything integrated with channeld
3. Launch channeld, and then the Unity server (Play -> Server only), and lastly the Unity client. Do not use the Host mode as it's not supported by channeld yet
4. Use W key to move forward; A/D to rotate the tank; mouse wheel to zoom in/out the camera
5. You can add AI-controlled tanks by clicking "Spawn Tanks" button on the server

*Tip*: to test the server and client in different Unity session, you can create a new Unity project that has [symlink to the *Assets* folder](https://support.unity.com/hc/en-us/articles/115003118426-Running-multiple-instances-of-Unity-referencing-the-same-project) of your Mirror project.

# How to integrate with your Mirror project
## Transport
ChanneldTransport is the core component that integrates Mirror with channeld. For both server and client, ChanneldTransport creates a connection to channeld, and do a couple of certain things to make sure the connection flow (authentication, scene loading, player spawning, etc.) happens correctly.

To use it, simply replace the Transport you are using in your Mirror project (KcpTransport by default) with [ChanneldTransport](Assets/channeld/ChanneldTransport.cs):

![](Assets/channeld/Doc/NetworkManager.png)

The [Tanks](https://mirror-networking.gitbook.io/docs/examples/tanks) is highly recommended for testing, as it's been primarily used for developing and debuging this project.

## GameState
By default, ChanneldTransport does nothing more than forwarding the Mirror messages in game. To utilize channeld's data pub/sub and fan-out feature, the developer need to define the structure of the channel data with Protobuf. 

The tank demo's [proto](https://github.com/indiest/channeld/blob/master/proto/example_mirror_tanks.proto) defines two types of sub-state: TransformState and TankState. They are corresponding to [ChanneldNetworkTransform](Assets/channeld/ChanneldNetworkTransform.cs) and [TankChanneld](Assets/channeld/Examples/Tanks/TankChanneld.cs) scripts.

ChanneldNetworkTransform is the replacement of Mirror's NetworkTransform. It replaces the RPCs with sending the TransformState update to channeld. Similarly, TankChanneld overrides the `SerializeSyncVars` to send the TankState update.

*Notice: `[SyncVar]` hooks is not implemented yet.*

GameState<T> is the abstract class that manages the channel data. For every type of channel data, the developer needs to implement a subclass of GameState<T> where T is the Protobuf-generated ChannelData class. For example, the [TankGameState](Assets/channeld/Examples/Tanks/TankGameState.cs) class uses `TankGameChannelData` as the type parameter. Then TankGameState implemented the `GetChannelDataUpdateFromTransform`, `GetTransformUpdateFromChannelData` and `Merge` methods to speicify how to convert between the transform update and the channel data update message, and how should two channel updates be merged, especially when there are maps involved.

To use a GameState, create a GameObject in the scene with the GameState<T> subclass attached, and set its Channel Type properly. You can have multiple GameState instances in your scene, but each Channel Type should only have one instance (except for the Spatial channels)!

For the final step, attach the [ChanneldInterestManagement](Assets/channeld/ChanneldInterestManagement.cs) script to your NetowrkManager. Now you are ready to go!

## Observers and Interest Management
[TODO]

## Multi-servers
Since channeld supports having multiple game servers connected to form one game world, sometimes the developer needs different transport configurations for different game servers. For example, in an MMO with many separate scenes, the master server's Server Channel Type should be Global, and the scene server's Server Channel Type should be Subworld. To achieve that, use the *`-sc`* argument to launch the headless server:

`MyServer.exe -batchmode -sc Subworld`

## Troubleshooting
### Client authoritative
To run some of the examples of Mirror(e.g. the Tank demo) with channeld properly, you'll need to use the client authoritative configuration, otherwise the game state will be not synchronized across the clients.

By default, channeld uses the client non-authoritative configuration. To replace it, use the *`-cfsm`* argument to launch channeld:

`go run . -cfsm ../config/client_authoratative_fsm.json`
