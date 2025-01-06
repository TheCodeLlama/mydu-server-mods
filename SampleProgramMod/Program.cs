using System;
using System.Net.Http;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BotLib.BotClient;
using BotLib.Generated;
using BotLib.Protocols;
using BotLib.Protocols.Queuing;
using Microsoft.Extensions.DependencyInjection;
using NQutils;
using NQutils.Config;
using NQutils.Logging;
using NQutils.Sql;
using Orleans;
using NQ.Interfaces;
using NQ;
using NQ.RDMS;
using NQ.Router;
using System.Threading.Channels;
using Backend;
using Backend.Business;

/// Simple serialisation Channel for one or multiple event sources
public class EventQueue: IDisposable
{
    private int nextIndex = 0;
    private Channel<(int, object)> channel = System.Threading.Channels.Channel.CreateUnbounded<(int, object)>();
    private List<Action> onDispose = new();
    public int Register<T>(Event<T> ev) where T:new()
    {
        var id = nextIndex++;
        Event<T>.MsgReceivedEventHandler cb = p => channel.Writer.TryWrite((id, p));
        ev.msgReceived += cb;
        onDispose.Add(()=>ev.msgReceived -= cb);
        return id;
    }
    public async Task<(int, object)> ReadAsync()
    {
        return await channel.Reader.ReadAsync();
    }
    public void Dispose()
    {
        foreach (var c in onDispose)
        {
            c();
        }
    }
}

/// Mod base class
public class Mod
{
    public static IDuClientFactory RestDuClientFactory => serviceProvider.GetRequiredService<IDuClientFactory>();
    /// Use this to acess registered service
    protected static IServiceProvider serviceProvider;
    /// Use this to make gameplay calls, see "Interfaces/GrainGetterExtensions.cs" for what's available
    protected static IClusterClient orleans;
    /// Use this object for various data access/modify helper functions
    protected static IDataAccessor dataAccessor;
    /// Conveniance field for mods who need a single bot
    protected Client bot;
    /// Create or login a user, return bot client instance
    public static async Task<Client> CreateUser(string prefix, bool allowExisting = false, bool randomize = true)
    {
        string username = prefix;
        if (randomize)
        {
            // Do not use random utilities as they are using tests random (that is seeded), and we want to be able to start the same test multiple times
            Random r = new Random(Guid.NewGuid().GetHashCode());
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz";
            username = prefix + '-' + new string(Enumerable.Repeat(0, 127 - prefix.Length).Select(_ => chars[r.Next(chars.Length)]).ToArray());
        }
        LoginInformations pi = LoginInformations.BotLogin(username, Environment.GetEnvironmentVariable("BOT_LOGIN"), Environment.GetEnvironmentVariable("BOT_PASSWORD"));
        return await Client.FromFactory(RestDuClientFactory, pi, allowExising: allowExisting);
    }
    /// Setup everything, must be called once at startup
    public static async Task Setup()
    {
        var services = new ServiceCollection();

        //services.RegisterCoreServices();
        var qurl = Environment.GetEnvironmentVariable("QUEUEING");
        if (qurl == "")
            qurl = "http://queueing:9630";
        services
        .AddSingleton<ISql, Sql>()
        .AddInitializableSingleton<IGameplayBank, GameplayBank>()
        .AddSingleton<ILocalizationManager, LocalizationManager>()
        .AddTransient<IDataAccessor, DataAccessor>()
        //.AddLogging(logging => logging.Setup(logWebHostInfo: false))
        .AddOrleansClient("IntegrationTests")
        .AddHttpClient()
        .AddTransient<NQutils.Stats.IStats, NQutils.Stats.FakeIStats>()
        .AddSingleton<IQueuing, RealQueuing>(sp => new RealQueuing(qurl, sp.GetRequiredService<IHttpClientFactory>().CreateClient()))
        .AddSingleton<IDuClientFactory, BotLib.Protocols.GrpcClient.DuClientFactory>()
        //.AddSingleton<Backend.AWS.IS3, Backend.AWS.FakeS3.FakeS3Singleton>()
        //.AddSingleton<Backend.Storage.IItemStorageService, Backend.Storage.ItemStorageService>()
        ;//.AddInitializableSingleton<IUserContent, UserContent>();
        var sp = services.BuildServiceProvider();
        serviceProvider = sp;
        await serviceProvider.StartServices();
        ClientExtensions.SetSingletons(sp);
        ClientExtensions.UseFactory(sp.GetRequiredService<IDuClientFactory>());
        orleans = serviceProvider.GetRequiredService<IClusterClient>();
        dataAccessor = serviceProvider.GetRequiredService<IDataAccessor>();
    }
    public async Task Start()
    {
        try
        {
            await Loop();
        }
        catch(Exception e)
        {
            Console.WriteLine($"{e}");
            throw;
        }
    }
    /// Override this with main bot code
    public virtual Task Loop()
    {
        return Task.CompletedTask;
    }
    /// Conveniance helper for running code forever
    public async Task SafeLoop(Func<Task> action)
    {
        while (true)
        {
            try
            {
                await action();
            }
            catch(Exception e)
            {
                Console.WriteLine($"Exception in mod action: {e}");
            }
        }
    }
    public async Task ChatDmOnMention(string key)
    {
        var listener = bot.Events.MessageReceived.Listener();
        while (true)
        {
            var msg = await listener.GetLastEventWait(mc=>true,1000000000);
            listener.Clear();
            if (msg.message.Contains(key))
            {
                await bot.Req.ChatMessageSend(new NQ.MessageContent
                        {
                            channel = new NQ.MessageChannel
                            {
                                channel = MessageChannelType.PRIVATE,
                                targetId = msg.fromPlayerId,
                            },
                            message = "You wanted to talk to me?",
                        });
            }
        }
    }
}

/**
   This mod will spawn a chat bot that can calirate all
   mining units of a construct, for a fee.
*/
public class ModCalibrationBot: Mod
{
    NQ.MessageChannel channel;
    EventListener<MessageContent> listener;
    public override async Task Loop()
    {
        bot = await CreateUser("calibratorBot", true, false);
        var pi = await orleans.GetPlayerGrain(bot.PlayerId).GetPlayerInfo();
        Console.WriteLine(pi.ToString());
        // Give infinite charges
        await orleans.GetPlayerGrain(bot.PlayerId).UpdatePlayerPropertyEx(
            "miningCalibrationCharges",
            new PropertyValue((long)10000000),
            true);
        channel = new NQ.MessageChannel
        {
            channel = MessageChannelType.PRIVATE
        };
        listener = bot.Events.MessageReceived.Listener();
        _ = ChatDmOnMention("@calibrationBot");
        await SafeLoop(Action);
    }
    public async Task Action()
    {
        // wait for DM
        var msg = await listener.GetLastEventWait(mc => mc.channel.channel == channel.channel,1000000);
        listener.Clear();
        //msg: fromPlayerId fromPlayerName message
        Int64.TryParse(msg.message, out var cid);
        // Get all mining unit element ids
        var els = await orleans.GetConstructElementsGrain((ulong)cid).GetElementsOfType<NQutils.Def.MiningUnit>();
        Console.WriteLine($"{els.Count} MiningUnit detected");
        ulong cost = (ulong)els.Count * 10000000;
        var dsg = orleans.GetDirectServiceGrain();
        // transfer money from requester to bot
        await dsg.WalletTransfer(msg.fromPlayerId,
            new WalletTransfer
            {
                fromWallet = EntityId.Player(msg.fromPlayerId),
                toWallet = EntityId.Player(bot.PlayerId),
                amount = cost,
                reason = "calibration fees",
            });
        // do the calibration
        foreach (var eid in els)
        {
            try
            {
                await orleans.GetMiningUnitGrain(eid).Calibrate(bot.PlayerId);
            }
            catch(Exception)
            { //cooldown, too bad
            }
        }
    }
}

/// This bot will accept barters and give you market value for your stuff
public class ModPawnShopBot: Mod
{
    /// Evaluate value of barter content
    private async Task<Currency> Evaluate(BarterState bs)
    {
        long res = 0;

        var mg = orleans.GetMarketGrain();
        // list markets on planet 2
        var ml = await mg.MarketGetList(2, 2);
        // pick one at random
        var targetMarket = ml.markets[0];
        
        foreach (var item in bs.items)
        {
            // Get orders for that item
            var msr = new MarketSelectRequest();
            msr.marketIds.Add(targetMarket.marketId);
            msr.itemTypes.Add(item.item.type);
            var orders = await mg.MarketSelectItem(msr, 2);
            //find best buy price
            long bestUPrice = 1;
            foreach( var o in orders.orders)
            {
                if (o.buyQuantity > 0 && o.unitPrice > bestUPrice)
                    bestUPrice = o.unitPrice.amount;
            }
            res += bestUPrice * item.quantity.quantity;
        }
        return res;
    }
    private EventListener<PlayerId> breq;
    private async Task SendPlayerUpdates()
    {
        while (true)
        {
            await bot.Req.PlayerUpdate(new PlayerUpdate
                {
                    playerId = bot.PlayerId,
                    constructId = 1000002,
                    position = new Vec3{x=16, y=16, z=16},
                    rotation = Quat.Identity,
                    time = TimePoint.Now(),
                });
            await Task.Delay(100);
        }
    }
    public override async Task Loop()
    {
        bot = await CreateUser("pawnShop", true, false);
        _ = SendPlayerUpdates();
        breq = bot.Events.BarterRequested.Listener();
        await SafeLoop(Action);
    }
    public async Task Action()
    {
        var m = await breq.GetLastEventWait(x =>true, 1000000000);
        breq.Clear();
        var pid = m.id;
        await bot.Req.BarterUpdate(
            new BarterSessionState
            {
                mine = new BarterState(),
                peer = new BarterState(),
            });
        // monitor all barter events
        using var eq = new EventQueue();
        eq.Register(bot.Events.BarterUpdated);
        eq.Register(bot.Events.BarterCanceled);
        eq.Register(bot.Events.BarterDone);
        while (true)
        {
            var (rid, payload) = await eq.ReadAsync();
            if (rid == 2 || rid == 1) // cancel or done
                break; //done
            var bss = (BarterSessionState)payload;
            var value = await Evaluate(bss.peer);
            await bot.Req.BarterUpdate(
                new BarterSessionState
                {
                    mine = new BarterState
                    {
                        accepted = true,
                        money = value,
                    },
                    peer = bss.peer,
                });
        }
    }
}

/// This Mod will teleport stuff from one container to an other
public class ModContainerTeleport: Mod
{
    private ulong source;
    private ulong target;
    // construct by givin source and target element ids
    public ModContainerTeleport(ulong source, ulong destination)
    {
        this.source = source;
        this.target = destination;
    }
    public override async Task Loop()
    {
        await SafeLoop(Action);
    }
    public async Task Action()
    {
        var sg = orleans.GetContainerGrain(source);
        var tg = orleans.GetContainerGrain(target);
        var tc = await tg.Get(2); // get target content as aphelia
        var targetFreeSlot = 0;
        foreach (var s in tc.content)
        {
            if (targetFreeSlot <= s.position)
                targetFreeSlot = s.position+1;
        }
        var sc = await sg.Get(2);
        if (sc.content.Count != 0)
        {
            var sourceSlot = sc.content[0].position;
            //await tg.SwapBetween(2, targetFreeSlot, sg, sourceSlot);
            await sg.SwapBetween(2, sourceSlot, tg,  targetFreeSlot);
        }
        await Task.Delay(1000);
    }
}


/** This chatbot mod allows construct owner to set a code
    that when entered give anybody RDMS access to the construc.
*/
public class ModConstrucCode: Mod
{
    // Check that player pid is owner of construct cid
    private async Task<bool> OwnerCheck(long cid, long pid)
    {
        var ci = await orleans.GetConstructInfoGrain(new ConstructId{constructId=(ulong)cid}).Get();
        if (ci.mutableData.ownerId.playerId != 0)
        {
            if (ci.mutableData.ownerId.playerId != (ulong)pid)
            {
                return false;
            }
        }
        else if (ci.mutableData.ownerId.organizationId != 0)
        {
            var esl = await orleans.GetOrganizationGrain(ci.mutableData.ownerId.organizationId).EffectiveSuperLegate();
            if (esl != (ulong)pid)
            {
                return false;
            }
        }
        else
            return false;
        return true;
    }
    public class Grant
    {
         public long Creator;
         public long TargetConstruct;
         public string Password;
         public List<PolicyId> Policies;
    }
    private List<Grant> state = new List<Grant>();
    NQ.MessageChannel channel; 
    EventListener<MessageContent> listener;
    public override async Task Loop()
    {
        bot = await CreateUser("codeBot", true, false);
        channel = new NQ.MessageChannel
        {
            channel = MessageChannelType.PRIVATE
        };
        _ = ChatDmOnMention("@codeBot");
        listener = bot.Events.MessageReceived.Listener();
        await SafeLoop(Action);
    }
    public async Task Action()
    {
        var msg = await listener.GetLastEventWait(mc => mc.channel.channel == channel.channel, 1000000);
        listener.Clear();
        //msg: fromPlayerId fromPlayerName message
        var words = msg.message.Split(' ');
        var replyChan = new NQ.MessageChannel
        {
            channel = MessageChannelType.PRIVATE,
            targetId = msg.fromPlayerId,
        };
        if (words[0] == "grant")
        {
            if (Int64.TryParse(words[1], out var cid))
            {
                //ownership check
                if (!await OwnerCheck((long)cid, (long)msg.fromPlayerId))
                {
                    await bot.Req.ChatMessageSend(new NQ.MessageContent
                        {
                            channel = replyChan,
                            message = "You are not the owner of this construct",
                        });
                    return;
                }
                var pwd = words[2];
                state.Add(new Grant {
                        Creator = (long)msg.fromPlayerId,
                        TargetConstruct = cid,
                        Password = pwd,
                        Policies = new List<PolicyId>(),
                });
                await bot.Req.ChatMessageSend(new NQ.MessageContent
                    {
                        channel = replyChan,
                        message = "Password is set",
                    });
            }
        }
        else if (words[0] == "access")
        {
            if (Int64.TryParse(words[1], out var cid))
            {
                var pwd = words[2];
                var entry = state.Where(e=>e.TargetConstruct == cid && e.Password == pwd).FirstOrDefault();
                if (entry == null || entry.Password != pwd)
                {
                    await bot.Req.ChatMessageSend(new NQ.MessageContent
                        {
                            channel = replyChan,
                            message = "ACCESS DENIED",
                        });
                    return;
                }
                var ci = await orleans.GetConstructInfoGrain((ulong)cid).Get();
                // append a tag and a policy to construct/rdms
                var rpg = orleans.GetRDMSRegistryGrain(ci.mutableData.ownerId);
                var tag = await rpg.CreateTag(new TagData
                    {
                        owner = ci.mutableData.ownerId,
                        name = $"Code grant of {cid} to {msg.fromPlayerName}",
                        description = $"Granted by code on {DateTime.Now}",
                    });
                var rag = orleans.GetRDMSAssetGrain(new AssetId{type = AssetType.Construct, construct = (ulong)cid});
                var atg = await rag.GetTagList(new AssetId{type = AssetType.Construct, construct = (ulong)cid});
                await rag.UpdateTags(new AssetUpdateTags
                    {
                        useItemHierarchy = atg.useItemHierarchy,
                        asset = new AssetId{type = AssetType.Construct, construct = (ulong)cid},
                        tags = atg.tags.Select(x=>x.tagId).ToList(),
                    });
                var p = new PolicyData
                {
                    owner = ci.mutableData.ownerId,
                    name = $"Code grant of {cid} to {msg.fromPlayerName}",
                    description = $"Granted by code on {DateTime.Now}",
                    actors = new List<ActorId>{new ActorId{actorId = msg.fromPlayerId, type = ActorType.Player}},
                    rights = new List<Right>{Right.ConstructBoard},
                    tags = new List<TagId>{tag},
                };
                var pid = await rpg.CreatePolicy(p);
                entry.Policies.Add(pid);
                await bot.Req.ChatMessageSend(new NQ.MessageContent
                    {
                        channel = replyChan,
                        message = "ACCESS GRANTED",
                    });
            }
        }
        else
        await bot.Req.ChatMessageSend(new NQ.MessageContent
            {
                channel = replyChan,
                message = "unknown command",
            });
    }
}

// Chatbot mod that makes player guess a number and gives rewards
public class ModChatGame: Mod
{
    public override async Task Loop()
    {
        bot = await CreateUser("guessingGame", true, false);
        var channel = new NQ.MessageChannel
        {
            channel = MessageChannelType.HELP
        };
        var rnd = new System.Random();
        using var listener = bot.Events.MessageReceived.Listener();
        while(true)
        {
            var target = rnd.Next(0, 100);
            await bot.Req.ChatMessageSend(new NQ.MessageContent
                {
                    channel = channel,
                    message = "Guess a number between 0 and 100 for a reward",
                });
            while(true)
            {
                var msg = await listener.GetLastEventWait(mc => mc.channel.channel == channel.channel, 1000000);
                listener.Clear();
                Console.WriteLine("GOT " + msg.message);
                //msg: fromPlayerId fromPlayerName message
                if (msg.message=="@constructId")
                {
                    var pu = await orleans.GetPlayerGrain(msg.fromPlayerId).GetPositionUpdate();
                    var pci = pu.localPosition.constructId;
                    await bot.Req.ChatMessageSend(new NQ.MessageContent
                        {
                            channel = channel,
                            message = $"{pci}",
                        });
                }
                else if (Int32.TryParse(msg.message, out var guess))
                {
                    if (guess == target)
                    {
                        var reward = rnd.Next(10000, 100000);
                        await bot.Req.ChatMessageSend(new NQ.MessageContent
                            {
                                channel = channel,
                                message = $"You guessed right {msg.fromPlayerName}, you earn {reward} talent points.",
                            });
                        // give talent points
                        await orleans.GetTalentGrain(msg.fromPlayerId).AddAvailable(reward);
                        await Task.Delay(5000);
                        break;
                    }
                    var err = guess > target ? "above" : "below";
                    await bot.Req.ChatMessageSend(new NQ.MessageContent
                        {
                            channel = channel,
                            message = $"You guessed wrong {msg.fromPlayerName}, you are {err} target.",
                        });
                }
            }
        }
    }
}

/// DoorMan will open a door if you bow to it
public class ModDoorMan: Mod
{
    private ulong constructId;
    private ulong elementId;
    private Vec3 pos;
    private ulong triggerAnim;
    // Constructor
    public ModDoorMan(
        ulong constructId, //< constructId the door is on
        ulong elementId,   //< elementId of the door
        Vec3 pos,          //< position of the doorman on construct
        ulong triggerAnim=197  //< anim to trigger door opening
        )
    {
        this.constructId = constructId;
        this.elementId = elementId;
        this.pos = pos;
        this.triggerAnim = triggerAnim;
    }
    public override async Task Loop()
    {
        bot = await CreateUser("doorMan", true, false);
        await Task.WhenAll(
            SendPlayerUpdates(),
            Monitor()
            );
    }
    private async Task SendPlayerUpdates()
    {
        while (true)
        {
            await bot.Req.PlayerUpdate(new PlayerUpdate
                {
                    playerId = bot.PlayerId,
                    constructId = constructId,
                    position = pos,
                    rotation = Quat.Identity,
                    time = TimePoint.Now(),
                });
            await Task.Delay(100);
        }
    }
    private async Task Monitor()
    {
        bool processing = false; // Don't repeat action while anim is on
        bot.Events.PlayerUpdates.msgReceived += async (msg) => {
            if (processing)
                return;
            foreach (var pu in msg.updates)
            {
                if (pu.animationState == triggerAnim)
                    Console.WriteLine($"{pu.playerId} with {pu.animationState}");
                if (pu.animationState == triggerAnim)
                {
                    processing = true;
                    try {
                        // open door
                    await orleans.GetConstructElementsGrain(constructId)
                        .UpdateElementProperty(new ElementPropertyUpdate
                            {
                                constructId = constructId,
                                elementId = elementId,
                                name = "element_state",
                                value = new PropertyValue(true),
                                timePoint = TimePoint.Now(),
                            });
                    } catch(Exception e)
                    {
                        Console.WriteLine($"{e}");
                        throw;
                    }
                    _ = Task.Delay(10000).ContinueWith(async t=>{
                            //close door
                            await orleans.GetConstructElementsGrain(constructId)
                        .UpdateElementProperty(new ElementPropertyUpdate
                            {
                                constructId = constructId,
                                elementId = elementId,
                                name = "element_state",
                                value = new PropertyValue(false),
                                timePoint = TimePoint.Now(), 
                            });
                        processing = false;
                    });
                }
            }
        };
        while (true)
            await Task.Delay(1000);
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        Config.ReadYamlFileFromArgs("mod", args);
        Mod.Setup().Wait();
        var t1 = (new ModCalibrationBot()).Start();
        var t2 = (new ModConstrucCode()).Start();
        var t3 = (new ModPawnShopBot()).Start();
        var t4 = (new ModChatGame()).Start();
        var t5 = (new ModContainerTeleport(360727, 360728)).Start();
        var t6 = (new ModDoorMan(1000750, 364601, 
            new Vec3{x= 131.72743922984228,y= 138.6301247658521,z= 130.1140020703897},
            197)).Start();

        Task.WhenAll(new List<Task>{ t1,t2,t3,t4,t5,t6}).Wait();
    }
}