# MyDU server modding toolkit

# Forewords

Mods are a powerful way to modify the behavior of a myDU server.

Never run untrusted code as mods, since mods have complete unrestricted access to you myDU server and game state, mod code is not sandboxed in any way! 

# Embedded dll server mod

These mods take the form of C# dlls that are loaded by the server’s Orleans component.

They have the capability to inject contextual entries to the client’s popup menus, and to inject Javascript code on each client, opening the door to new UI.

## Getting started

Download the mod toolkit archive and run “dotnet build” in the “SampleDllMod” directory which contains a sample mod.

Copy the generated ModAdmin.dll in the Mods directory at the root of the mydu server install directory.

You can have as many mods as you want, the dll name must start with “Mod” prefix.

Restart the server stack and connect to it. Admin players should see new menu entries when right-clicking in the game.

## Workflow

Each mod dll must contain a class MyDuMod implementing the IMod interface.

The GetName() method must return a short string with the mod’s name, that must be unique amongst the enabled mods.

The “Initialize” method of IMod is called once at initialization time upon first player connection.

It is given a IServiceProvider that allows you to access mydu services (and register your owns for sharing between mods).

When a player connects, “GetModInfoFor” is called on every mod and must return a ModInfo (or null for nothing) containing the mod actions to add in that player’s right-click menus.

You can use a backslash in ModActionDefinition’s label to create sub-menu entries.

When a player click on one of those entries, “TriggerAction” is called on the target mod.

Be aware that multiple tasks can call TriggerAction at the same time, your code must be able to handle this scenario.

## Interfacing with client javascript

As illustrated by the ModAdmin example, a mod can send javascript events to a client by using the IPub interface and push a ModTriggerHubEventRequest to it.

There is an event name already registered in each client: “modinjectjs” that will eval() the payload.

The javascript code has access to a bound method “CPPMod.sendAction” which will invoke TriggerAction() on the target mod. It’s payload is as follows:

```jsx
CPPMod.sendModAction(”modName”, .modActionId, 
    [constructId, elementId, playerId], payload)
```

The recommended way to exchange arbitrary informations is to serialize them in json in the payload argument.

# External program server mod

Those mods take the form of C# programs that run alongside the server side and connect to it’s services

## What you can do with it

The mod C# code has access to:

- DU’s internal gameplay API allowing to inspect and modify many aspects of the game state.
- A client API allowing code to perform gameplay actions as a logged in player

## What you technically can’t do with it

- Modify the client or communication protocol in any way
- Change existing gameplay code
- Voxel operations

## Writing mods

### Prerequisites

All that is required is a dotnet SDK ≥ version 6, and your favorite IDE.

### Setting things up

Download the mod toolkit archive and extract it anywhere. Go to the “Examples” directory and run “dotnet build”. Have a look at the “Program.cs” which contains basic examples of what you can do.

### The gameplay API

The gameplay API is accessible through Microsoft Orlean’s middleware.

The file “Interfaces/GrainGetterExtensions.cs” lists the methods you can call on the Mod class’s “orleans” member to acquire a specific interface. The interfaces are all in eponymous file names in that same folder. Giving 10 talent points to player with id 42 is as simple as

```jsx
await orleans.GetTalentGrain(42).AddAvailable(10);
```

### The client API

To connect a code-controlled player to the server, use the “CreateUser” method in the “Mod” class:

```csharp
var myBot = await CreateUser(login, true, false);
```

### Sending commands to the server

The bot instance has a “Req” member with one async method for each possible message that can be sent to the server.

The list is long and available in ‘RequestFlowProtocol.yaml’. As an example here is how to send a chat message:

```csharp
await myBot.Req.ChatMessageSend(new NQ.MessageContent
{
     channel = new NQ.MessageChannel
     {
          channel = MessageChannelType.HELP
     },
     message = "Hello, I’m a helpful assistant",
});
```

The data structures used as argument and return values of these functions are spread in the def files in “Interfaces””.

### Receiving events from the server

By default all messages initiated by the server to the client are dropped. Use the “Events” objects on the bot to receive them, for instance to get all chat messages:

`using var listener = myBot.Events.MessageReceived.Listener();`

`var msg = await listener.GetLastEventWait();`

GetLastEventWait() accepts an optional delegate that can be used to filter events based on their payload.

Alternatively one can use the “EventQueue” class to receive and enqueue multiple messages types, or directly register a delegate to the msgReceived event.

## Deploying mods

### Test mode

The mod code needs to access the myDU server using Queuing Service then front for client API, and orlean’s internal communication port for gameplay API (30000).

Easiest option is to modify the “ports” section of orleans to add “127.0.0.1:30000:30000” so that the mod program running on the host can access it.

The mods needs two environment variables:BOT_LOGIN and BOT_PASSWORD with credentials to a user with “Bot”, “game” and optionally “staff” roles. The “staff” role allows the bot to impersonate and create any player. The “Bot” role is required to allow a bot to connect using that account.

In test mode you also need to specify the queueing service url.

Then run your mod with

```bash
BOT_LOGIN=bot BOT_PASSWORD=secret \
QUEUEING=http://localhost:9630 dotnet run ./dual.yaml
```

### Production mode

Use the provided “Dockerfile.mod” to build a docker image containing your mod binary by running in the mod directory:

```bash
docker build -t mycoolmod:1.0 -f Dockerfile.mod .
```

Optionally push the image to a docker registry so that servers can access it.

Then add a new service to the docker-compose file of the target myDU server installation. It should look like:

```
mycoolmod:
    image: mycoolmod:1.0
    command: /config/dual.yaml
    volumes:
      - ${CONFPATH}:/config
    restart: always
    environment:
	    BOT_LOGIN: bot
	    BOT_PASSWORD: secret
    networks:
      vpcbr:
        ipv4_address: 10.5.0.XX
```

replacing the XX by a not yet used value

Finally when upping the stack, wait a few seconds  and run “docker-compose up -d”.

For convenience one can update up.sh / up.ba[t](http://up.sh/up.bat) to start the additional service

# Webhook mod and REST API

One can implement a mod as a HTTP REST webhook. This is not recommended for high-throughput scenarios as there is significant overhead.

## Mod server routes

Your mod is implemented as a HTTP(s) webserver that must be accessible to the orleans container.

It needs to implement two POST routes with json input/output:

/getmodinfo : take an object with fields “playerId” and “isPlayerAdmin”, return a ModInfo

/action/<playerId> : take a ModAction, returns nothing

## Making Orleans call from the mod

The Orleans 10111 port must be reachable to your mod. Options are:

- Run inside the docker stack
- Expose the port (do not expose it publicly)
- Route through nginx proxy by modifying its configuration (no public exposure: limit IP or basic HTTP auth for instance)

Then POST to /meta/metacall with json (or NQ packed binary) as input/output.

Input schema: 

```
public class MetaCall
{
    public string interfaceName;
    public string interfaceKeyString;
    public long interfaceKeyLong;
    public string methodName;
    public List<string> serializedArgs;
}

```

Note that  method call arguments must be serialized in json (so that’s json in json) in serializedArgs.

Use the provided Interface folder for structs and function available

Output: json with result: or error: fields

## Forwarding messages to players

POST to /meta/metamessage with following payload structure:

```
public class MetaMessage
{
    public string targetType;
    public ulong targetId;
    public string requestName;
    public string serializedPayload;
}

```

“targetType” can be “player” or “construct”, “requestName” must be one of the ServerToClient request names in requestFlowProtocol.yaml. “serializedPayload” is the json serialization of the appropriate argument structure.

## Registering your mod

Call /meta/registermod with json object containing “modName” (mod name) and “webhookBaseUrl”