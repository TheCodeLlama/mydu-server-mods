using Orleans;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Backend;
using Backend.Storage;
using Backend.Scenegraph;
using NQ;
using NQ.Interfaces;
using NQutils;
using NQutils.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

// for json deserialization of client input
public class Quantas
{
    public ulong quantas;
}

//Mod class, must be called MyDuMod and implements IMod
public class MyDuMod: IMod, ISubObserver
{
    // You should probably load an external .js file instead,
    // to be able to make changes without reloading the whole stack
    private readonly string panel = @"
    console.warn(""AdminPanel injection"");
    class AdminPanel extends MousePage
    {
      constructor()
      {
        console.warn(""AdminPanel ctor"");
        super();
        this._createHTML();
        this.wrapperNode.classList.add(""hide"");
        engine.on(""AdminPanel.show"", this.show, this);
        this.notificationIcon = new NotificationIconComponent(""icon_missing"", ""admin"");
        var that = this;
        this.notificationIcon.onClickEvent.subscribe(() => that.show(true));
        hudManager.addNotificationIcon(this.notificationIcon);
        this.notificationIcon.HTMLNodes.icon.innerText = ""A"";
      }
      show(isVisible)
      {
          console.warn(""AdminPanel show"");
          super.show(isVisible);
      }
      _onVisibilityChange()
      {
        super._onVisibilityChange();
        console.warn(""OVC "" + this.isVisible);
        this.wrapperNode.classList.toggle(""hide"", !this.isVisible);
      }
      _close()
      {
          this.show(false);
      }
      _createHTML()
      {
          this.HTMLNodes = {};
          this.wrapperNode = createElement(document.body, ""div"", ""mining_unit_panel"");

          let header = createElement(this.wrapperNode, ""div"", ""header"");
          this.HTMLNodes.panelTitle = createElement(header, ""div"", ""panel_title"");
          this.HTMLNodes.panelTitle.innerText = ""Admin Panel"";
          this.HTMLNodes.closeIconButton = createElement(header, ""div"", ""close_button"");
          this.HTMLNodes.closeIconButton.addEventListener(""click"", () => this._close());

          createSpriteSvg(""icon_close"", ""icon_close"", this.HTMLNodes.closeIconButton);
          let content = createElement(this.wrapperNode, ""div"", ""content"");
          content.style.display = 'block';
          let wrapper = createElement(content, ""div"", ""content_wrapper"");
          this.HTMLNodes.qinput = createElement(wrapper, ""input"");
          this.HTMLNodes.qinput.type = ""text"";
          let button = createElement(wrapper, ""div"", ""generic_button"");
          button.innerText = ""give quantas"";

          button.addEventListener(""click"", ()=>this.giveQuantas());
          createElement(content, ""br"");
          let wrapper2 = createElement(content, ""div"", ""content_wrapper"");
          this.HTMLNodes.jinput = createElement(wrapper2, ""input"");
          this.HTMLNodes.jinput.type = ""text"";
          let button2 = createElement(wrapper2, ""div"", ""generic_button"");
          button2.innerText = ""eval"";
          createElement(content, ""br"");
          this.HTMLNodes.joutput = createElement(content, ""div"");
          button2.addEventListener(""click"", ()=>this.evaljs());
      }
      evaljs()
      {
          let value = this.HTMLNodes.jinput.value;
          let output = eval(value);
          this.HTMLNodes.joutput.innerText = output;
      }
      giveQuantas()
      {
          let value = parseInt(this.HTMLNodes.qinput.value);
          CPPMod.sendModAction(""Admin"", 10, [], JSON.stringify({quantas:value}));
      }
    }
    let adminPanel = new AdminPanel();
    ";

    private IServiceProvider isp;
    private IClusterClient orleans;
    private ILogger logger;
    private System.Random _rnd = new System.Random();

    public string GetName()
    {
        return "Admin";
    }

    public Task Initialize(IServiceProvider isp)
    {
        this.isp = isp;
        this.orleans = isp.GetRequiredService<IClusterClient>();
        this.logger = isp.GetRequiredService<ILogger<MyDuMod>>();
        return Task.CompletedTask;
    }

    /// Return a ModInfo to a connecting client
    public Task<ModInfo> GetModInfoFor(ulong playerId, bool admin)
    {
        var res = new ModInfo
        {
            name = "Admin",
            actions = new List<ModActionDefinition>
            {
                new ModActionDefinition
                {
                    id = 201,
                    label = "Info\\Get construct id",
                    context = ModActionContext.Construct,
                },
                new ModActionDefinition
                {
                    id = 202,
                    label = "Info\\Get element id",
                    context = ModActionContext.Element,
                },
                new ModActionDefinition
                {
                    id = 203,
                    label = "Info\\Get player id",
                    context = ModActionContext.Avatar,
                },
                new ModActionDefinition
                {
                    id = 301,
                    label = "Thievery\\Pick lock",
                    context = ModActionContext.Element,
                },
            },
        };
        // Only send this stuff to players flagged as admin in the BO
        if (admin)
            res.actions.AddRange(new List<ModActionDefinition>
                {
                    new ModActionDefinition
                    {
                        id = 1,
                        label = "Admin\\disown construct",
                        context = ModActionContext.Construct,
                    },
                    new ModActionDefinition
                    {
                        id = 2,
                        label = "Admin\\repair element",
                        context = ModActionContext.Element,
                    },
                    new ModActionDefinition
                    {
                        id = 3,
                        label = "Admin\\kill player",
                        context = ModActionContext.Avatar,
                    },
                    new ModActionDefinition
                    {
                        id = 5,
                        label = "Admin\\test\\inject js loop for testing",
                        context = ModActionContext.Global,
                    },
                    new ModActionDefinition
                    {
                        id = 6,
                        label = "Admin\\inject admin panel",
                        context = ModActionContext.Global,
                    },
                    new ModActionDefinition
                    {
                        id = 7,
                        label = "Admin\\open admin panel",
                        context = ModActionContext.Global,
                    },
                    new ModActionDefinition
                    {
                        id = 66,
                        label = "Admin\\test\\hook all events",
                        context = ModActionContext.Global,
                    },
                    new ModActionDefinition
                    {
                        id = 401,
                        label = "Admin\\boum construct",
                        context = ModActionContext.Construct,
                    },
                    new ModActionDefinition
                    {
                        id = 402,
                        label = "Admin\\external notif",
                        context = ModActionContext.Construct,
                    },
                    new ModActionDefinition
                    {
                        id = 403,
                        label = "Admin\\fake rain",
                        context = ModActionContext.Avatar,
                    },
                    new ModActionDefinition
                    {
                        id = 404,
                        label = "Admin\\fly",
                        context = ModActionContext.Construct,
                    },
                });
        return Task<ModInfo>.FromResult(res);
    }

    private ulong sid = 100000000; // client has a dedup mechanism, don't reuse
    protected async Task FakeRain(ulong fromPlayerId, ulong toPlayerId)
    { // Show a rain of fake shots only to the two players in argument
      // No shot is actually fired, a WeaponShot message is simply sent
      // to both clients which only serves for visual feedback
        var sg = isp.GetRequiredService<IScenegraph>();
        var pub = isp.GetRequiredService<IPub>();
        var bank = isp.GetRequiredService<IGameplayBank>();
        var (fr, fa) = await sg.GetPlayerWorldPosition(fromPlayerId);
        var (tr, ta) = await sg.GetPlayerWorldPosition(toPlayerId);
        var ws = new WeaponShot
        {
            originConstructId = fr.constructId,
            weaponId = 0,
            weaponType = bank.GetDefinition("WeaponMissileLarge1").Id,
            ammoType = bank.GetDefinition("AmmoMissileLargeAntimatterAdvancedAgile").Id,
            originPositionLocal = fr.position,
            originPositionWorld = fa.position,
            targetConstructId = tr.constructId,
            impactPositionLocal = tr.position,
            impactPositionWorld = ta.position,
        };
        for (int i=0; i<10; i++)
        {
            ws.id = sid++;
            await pub.NotifyTopic(Topics.PlayerNotifications(fromPlayerId),
                    new NQutils.Messages.WeaponShot(ws)
                    );
            await pub.NotifyTopic(Topics.PlayerNotifications(toPlayerId),
                    new NQutils.Messages.WeaponShot(ws)
                    );
            await Task.Delay(300);
        }
    }

    protected async Task Boum(ulong constructId)
    { // PvP weapon hit on a random construct element, with damages
        var bank = isp.GetRequiredService<IGameplayBank>();
        var elts = await orleans.GetConstructElementsGrain(constructId).GetVisibleAt(0);
        var idx = _rnd.Next(0, elts.elements.Count);
        var wf = new WeaponFire
        {
            playerId = 2,
            weaponId = 0,
            constructId = 10000,
            seatId = 0,
            targetId = constructId,
            impactPoint = elts.elements[idx].position,
            impactElementId = elts.elements[idx].elementId,
            impactElementType = elts.elements[idx].elementType,
            bboxCenterLocal = elts.elements[idx].position,
            bboxSizeLocal = new Vec3 { x = 16, y = 16, z = 16},
        };
        var killablePlayers = await orleans.GetConstructGrain(constructId).GetKillablePlayerListAndPosition();
        var voxelResult = await orleans.GetDirectServiceGrain().MakeVoxelDamages(wf, bank.GetBaseObject<NQutils.Def.Ammo>(bank.GetDefinition("AmmoMissileLargeAntimatterAdvancedAgile").Id), 100000, killablePlayers);
        if (voxelResult.damageOutput != null)
        {
            var deathInfoPvp = new PlayerDeathInfoPvPData
            {
                weaponId = 0,
                weaponTypeId = 0,
                constructId = 10000,
                constructName = "Solar defense",
                playerId = 2,
                playerName = "Aphelia",
                ownerId = new EntityId { playerId = 2},
            };
            var deathInfo = new PlayerDeathInfo
            {
                reason = DeathReason.WeaponShot,
            };
            var damageResult = await orleans.GetConstructDamageElementsGrain(constructId).ApplyPvpElementsDamage(voxelResult.damageOutput.elements, deathInfoPvp);
            foreach (var player in voxelResult.damageOutput.deadPlayers)
            {
                var playerGrain = orleans.GetPlayerGrain(player);
                await playerGrain.PlayerDieOperation(deathInfo);
            }
        }
    }

    public async Task TriggerAction(ulong playerId, ModAction action)
    { // Called when a player clicks on one of you Mod's popup entries
        if (action.actionId == 201)
        { // Send a popup to player with the construct id
            await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
            new NQutils.Messages.PopupReceived(new PopupMessage
                {
                    message = $"Constructid: {action.constructId}",
                    target = playerId,
                }));
        }
        if (action.actionId == 202)
        {
            await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
            new NQutils.Messages.PopupReceived(new PopupMessage
                {
                    message = $"ElementId: {action.elementId}",
                    target = playerId,
                }));
        }
        if (action.actionId == 203)
        {
            await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
            new NQutils.Messages.PopupReceived(new PopupMessage
                {
                    message = $"PlayerId: {action.playerId}",
                    target = playerId,
                }));
        }
        if (action.actionId == 301)
        { // lockpick
            // Note: for this to work you need to add a "LockPick" item in the Item Hierarchy, under 'Parts',
            // and give some lockpicks in your inventory.
            var tc = action.constructId;
            var te = action.elementId;
            // check in item bank if this is a door
            var bank = isp.GetRequiredService<IGameplayBank>();
            var ei = await orleans.GetConstructElementsGrain(tc).GetElement(te);
            var edef = bank.GetDefinition(ei.elementType);
            if (!edef.Is<NQutils.Def.DoorUnit>())
            { // bad element type, notify player
                await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
                    new NQutils.Messages.ModTriggerHudEventRequest(new ModTriggerHudEvent
                        {
                            eventName = "modinjectjs",
                            eventPayload = "CPPHud.addFailureNotification(\"This is not a door\");",
                        }));
                return;
            }
            // consume one lockpick from inventory
            var pig = orleans.GetPlayerInventoryGrain(playerId);
            var itemStorage = isp.GetRequiredService<IItemStorageService>();
            try
            {
                await using var transaction = await itemStorage.MakeTransaction(
                    Tag.HttpCall("lockpick") // use a dummy tag, those only serves for logging/tracing
                    );
                await pig.GiveOrTakeItems(transaction,
                    new List<ItemAndQuantity>() {
                        new ItemAndQuantity
                        {
                            item = new ItemInfo
                            {
                              type = bank.GetDefinition("LockPick").Id,
                            },
                            quantity = -1,
                        },
                    },
                    new());
                await transaction.Commit(); // do not forget that line!
            }
            catch (Exception e)
            { // Failure, one could filter on BusinessException with code InventoryNotEnough to distinguish
              // server errors from 'no lockpik in inventory'.
                logger.LogWarning(e, "lockpick failure");
                await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
                    new NQutils.Messages.ModTriggerHudEventRequest(new ModTriggerHudEvent
                        {
                            eventName = "modinjectjs",
                            eventPayload = "CPPHud.addFailureNotification(\"No lockpick available\");",
                        }));
                return;
            }
            // lockpick consumed, open door
            await orleans.GetConstructElementsGrain(tc).UpdateElementProperty(new ElementPropertyUpdate
                            {
                                constructId = tc,
                                elementId = te,
                                name = "element_state",
                                value = new PropertyValue(true),
                                timePoint = TimePoint.Now(), 
                            });
        }
        // Always recheck admin status as a bot could force-invoke an action
        // even if not received in your ModInfo
        if (!await orleans.GetPlayerGrain(playerId).IsAdmin())
            return;
        if (action.actionId == 1)
        { // disown construct
            await orleans.GetConstructGrain(action.constructId).ConstructSetOwner(0, new ConstructOwnerSet{ownerId = new EntityId()}, false);  
        }
        else if (action.actionId == 2)
        { //repair element. Don't do that on destroyed core units!
            await orleans.GetConstructElementsGrain(action.constructId)
                .UpdateElementProperty(new ElementPropertyUpdate
                    {
                        timePoint = TimePoint.Now(),
                        elementId = action.elementId,
                        constructId = action.constructId,
                        name = "hitpointsRatio",
                        value = new PropertyValue(1.0),
                    });
        }
        else if (action.actionId == 3)
        { // kill player
            await orleans.GetPlayerGrain(action.playerId).PlayerHardRespawn();
        }
        else if (action.actionId == 5)
        {
            // roundtrip loop check
            await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
            new NQutils.Messages.ModTriggerHudEventRequest(new ModTriggerHudEvent
                {
                    eventName = "modinjectjs",
                    eventPayload = "window.setInterval(function() {console.warn(\"Still alive\");}, 2000);",
                    //eventPayload = "window.setInterval(function() {CPPMod.sendModAction(\"amod\", 42, [1, 2, 3], \"\");}, 2000);",
                }));
        }
        else if (action.actionId == 6)
        { // inject the admin panel code
            await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
            new NQutils.Messages.ModTriggerHudEventRequest(new ModTriggerHudEvent
                {
                    eventName = "modinjectjs",
                    eventPayload = panel,
                }));
        }
        else if (action.actionId == 7)
        { // open the admin panel
            await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
            new NQutils.Messages.ModTriggerHudEventRequest(new ModTriggerHudEvent
                {
                    eventName = "AdminPanel.show",
                    eventPayload = "1",
                }));
        }
        else if (action.actionId == 10)
        { // admin panel give quantas action
            var js = action.payload;
            var data = JsonConvert.DeserializeObject<Quantas>(js);
            logger.LogInformation($"got payload of {data.quantas} quantas");
            var dsg = orleans.GetDirectServiceGrain();
            // transfer money
            await dsg.WalletTransfer(playerId,
                new WalletTransfer
                {
                    fromWallet = EntityId.Player(2), //Aphelia is filthy rich, she won't mind :p 
                    toWallet = EntityId.Player(playerId),
                    amount = data.quantas * 100, // backend uses cents
                    reason = "admin give",
                });
        }
        else if (action.actionId == 66)
        { // execute order 66: route ALL client analytics here
            await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
            new NQutils.Messages.ModEventRegistrationRequest(new ModEventRegistration
                {
                    modName = "Admin",
                    modAction = 67,
                    registrationId = 1,
                    eventGlob = "*",
                }));
        }
        else if (action.actionId == 67)
        {
            logger.LogInformation("ANALYTICS: " + action.payload);
        }
        else if (action.actionId == 401)
        {
            _ = Boum(action.constructId);
        }
        else if (action.actionId == 402)
        { // watch construct events to notify, will call OnSubscriptionMessageReceived
            var sub = isp.GetRequiredService<ISub>();
            await sub.Subscribe(
                Topics.ConstructPlayers(action.constructId),
                this);
            await sub.Subscribe(
                Topics.ConstructDetailsUpdate(action.constructId),
                this);
        }
        else if (action.actionId == 403)
        {
            _ = FakeRain(playerId, action.playerId);
        }
        else if (action.actionId == 404)
        {
            var cid = action.constructId;
            var v = new Vec3{x=0, y=0, z=10};
            var av = new Vec3{x=1, y=1, z=100};
            await orleans.GetConstructGrain(cid).SetResumeState(av, v, false);
            await Task.Delay(1000);
            var ci = await orleans.GetConstructInfoGrain(cid).Get();
            var cu = new ConstructUpdate
            {
                constructId = cid,
                baseId = ci.rData.parentId,
                position = ci.rData.position,
                rotation = ci.rData.rotation,
                worldRelativeVelocity = v,
                worldAbsoluteVelocity = v,
                worldRelativeAngVelocity = av,
                worldAbsoluteAngVelocity = av,
                pilotId = 0,
                grounded = false,
                time = TimePoint.Now(),
            };
            await isp.GetRequiredService<IPub>().NotifyTopic(Topics.PlayerNotifications(playerId),
                new NQutils.Messages.ConstructTransferControl(cu));
        }
    }

    public ConcurrentDictionary<ulong, DateTime> notifs = new();
    public string GetObserverKey() => "ModAdmin"; // From ISubObserver, UID
    public async Task OnSubscriptionMessageReceived(PubSubTopic topic, AbstractPacket message)
    {
         var parser = new NQutils.Messages.Parser(message);
         string extra = "";
         if (parser.ElementsChanged(out var ec)) //of type ElementOperation
         {
             extra = $"element change";
         }
         logger.LogInformation($"PUBSUB {topic.Exchange} {message.MessageType}");
         var cid = ulong.Parse(topic.RoutingKey);
         var cn = (await orleans.GetConstructInfoGrain(cid).Get()).rData.name;
         // rate limit
         if (notifs.TryGetValue(cid, out var dt) && dt >= DateTime.Now.Subtract(TimeSpan.FromMinutes(1)))
             return;
         notifs[cid] = DateTime.Now;
         // spawn external process, whose implementation is left as an exercise to the reader
         var opts = new ProcessStartInfo
         {
             FileName = "/OrleansGrains/Mods/external-notification",
             Arguments = $"'Something is happening on {cn}: {extra} {message.MessageType}'" ,
             UseShellExecute = false,
             RedirectStandardOutput = false,
             RedirectStandardError = false
         };
         try {
             using var proc = Process.Start(opts);

             await proc.WaitForExitAsync();
         }
         catch (Exception e)
         {
             logger.LogError(e, "Notification error");
         }
    }
}