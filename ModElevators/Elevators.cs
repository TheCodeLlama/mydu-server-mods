using Orleans;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using Backend;
using Backend.Business;
using Backend.Database;
using NQutils.Config;
using Backend.Construct;
using Backend.Storage;
using Backend.Scenegraph;
using NQ;
using NQ.RDMS;
using NQ.Interfaces;
using NQ.Visibility;
using NQ.Grains.Core;
using NQutils;
using NQutils.Exceptions;
using NQutils.Net;
using NQutils.Serialization;
using NQutils.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using MathNet.Spatial.Euclidean;
using MathNet.Numerics;


public class MyDuMod: IMod
{
    private IServiceProvider isp;
    private IClusterClient orleans;
    private ILogger logger;
    private IGameplayBank bank;
    private NQ.Visibility.Internal.InternalClient client;
    private IScenegraph scenegraph;
    private IPub pub;
    public string GetName()
    {
        return "Elevator";
    }
    public Task Initialize(IServiceProvider isp)
    {
        this.isp = isp;
        this.orleans = isp.GetRequiredService<IClusterClient>();
        this.logger = isp.GetRequiredService<ILogger<MyDuMod>>();
        this.bank = isp.GetRequiredService<IGameplayBank>();
        this.client = isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>();
        this.scenegraph = isp.GetRequiredService<IScenegraph>();
        this.pub = isp.GetRequiredService<IPub>();
        return Task.CompletedTask;
    }
    public Task<ModInfo> GetModInfoFor(ulong playerId, bool admin)
    {
        return Task.FromResult<ModInfo>(new ModInfo
            {
                name = GetName(),
                actions = new List<ModActionDefinition>
                {
                    new ModActionDefinition
                    {
                        id = 99,
                        label = "Elevator",
                        context = ModActionContext.Construct,
                    },
                    new ModActionDefinition
                    {
                        id = 500,
                        label = "Rotate Open",
                        context = ModActionContext.Construct,
                    },
                    new ModActionDefinition
                    {
                        id = 501,
                        label = "Rotate Close",
                        context = ModActionContext.Construct,
                    },
                    new ModActionDefinition
                    {
                        id = 502,
                        label = "Try secret door",
                        context = ModActionContext.Construct,
                    },
                }
            });
    }
    public static NQ.Quat ToQuatFromAngle(NQ.Vec3 v, double angle )
    {
        var q =  System.Numerics.Quaternion.CreateFromAxisAngle(
            new System.Numerics.Vector3((float)v.x, (float)v.y, (float)v.z), (float)angle);
        return new NQ.Quat { x=q.X, y = q.Y, z=q.Z, w=q.W};
    }
    public static NQ.Quat ToQuatFromDirection(NQ.Vec3 v, double roll = 0, bool fromZ = true)
    {
        NQ.Vec3 fdir = fromZ ? new NQ.Vec3{ z = 1} : new NQ.Vec3{ y = 1};
        var rotaxis = fdir.Cross(v.Normalized());
        var angle = Math.Atan2(rotaxis.Norm(), fdir.Dot(v));
        return ToQuatFromAngle(rotaxis.Normalized(), angle);
    }
    public static NQ.Vec3 ToEulerAngles(NQ.Quat q)
    {
        var e = ((Quaternion)q).ToEulerAngles();
        return new NQ.Vec3
        {
            x = e.Alpha.Radians,
            y = e.Beta.Radians,
            z = e.Gamma.Radians,
        }; // or is it?
    }
    public async Task Rotate(ulong playerId, ulong constructId, bool forward)
    {
        var cgrain = orleans.GetConstructInfoGrain(constructId);
        //var crd = (await cgrain.Get()).rData;
        var crl = await isp.GetRequiredService<IScenegraphAPI>().GetConstructPosition(constructId); 
        var elts = await orleans.GetConstructElementsGrain(constructId).GetVisibleAt(0);
        var coreEl = elts.elements[0];
        var coreRel = new NQ.RelativeLocation
        {
            constructId = constructId,
            position = coreEl.position,
            rotation = coreEl.rotation,
        };
        ConstructUpdate cu = null;
        var coreAbs = await scenegraph.ResolveRelativeLocation(coreRel, crl.constructId);
        for (int i=0; i<40;i++)
        {
            double angleDeg = (double)(i+1)*4.5/2.0 * (forward ? 1.0 : -1.0);
            var rot = ToQuatFromAngle(new NQ.Vec3{z=1}, angleDeg * Math.PI / 180.0);
            NQ.Quat newRot = (Quaternion)rot * (Quaternion)crl.rotation;
            NQ.Vec3 oldPos = ((Quaternion)crl.rotation).Rotate((Vector3D)coreEl.position);
            NQ.Vec3 newPos = ((Quaternion)newRot).Rotate((Vector3D)coreEl.position);
            var tmpRel = new NQ.RelativeLocation
            {
                constructId = constructId,
                position = coreEl.position,
                rotation = newRot,
            };
            //var tmpAbs = await scenegraph.ResolveRelativeLocation(tmpRel, crd.parentId);
            var compensate = oldPos - newPos;
            cu = new ConstructUpdate
            {
                constructId = constructId,
                baseId = crl.constructId,
                position = crl.position + compensate,
                rotation = newRot,
                worldRelativeVelocity = compensate / (double)(i+1) / 20.0,
                worldAbsoluteVelocity = compensate / (double)(i+1) / 20.0,
                worldRelativeAngVelocity = new NQ.Vec3{ z = 45.0},//*Math.Sign(angleDeg)*Math.PI/180.0 },
                worldAbsoluteAngVelocity = new NQ.Vec3{ z = 45.0},//*Math.Sign(angleDeg)*Math.PI/180.0 },
                grounded = false,
                pilotId = 2,
                time = TimePoint.Now(),
            };
            logger.LogInformation("go for update at {position} angles {angles}", cu.position, ToEulerAngles(cu.rotation));
            await isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>()
                .UpdateConstructAsync(NQutils.Serialization.Grpc.MakeEvent(new NQutils.Messages.ConstructUpdate(cu)));
            await Task.Delay(50);
        }
        for (int i=0; i<10;i++)
        {
            cu.worldRelativeAngVelocity = new NQ.Vec3();
            cu.worldAbsoluteAngVelocity = new NQ.Vec3();
            await isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>()
                .UpdateConstructAsync(NQutils.Serialization.Grpc.MakeEvent(new NQutils.Messages.ConstructUpdate(cu)));
            await Task.Delay(100);
        }
    }
    public async Task Elevate(ulong playerId, ulong parentConstruct)
    {
        var rploc = await scenegraph.GetPlayerLocation(playerId);
        var pcgrain = orleans.GetConstructInfoGrain(rploc.constructId);
        var crd = (await pcgrain.Get()).rData;
        var aploc = await scenegraph.ResolveWorldLocation(rploc);
        var pploc = await scenegraph.ResolveRelativeLocation(aploc, parentConstruct);
        var elevatorConstructId = rploc.constructId;
        var elts = await orleans.GetConstructElementsGrain(parentConstruct).GetVisibleAt(0);
        double bestZTarget = 128.0;
        double bestDist = 100000.0;
        // Find closest but not too close XS adjustor as Z target
        foreach (var el in elts.elements)
        {
            if (el.elementType != 2648523849)
                continue;
            var dist = el.position.Dist(pploc.position);
            if (dist < 5.0)
                continue;
            if (dist < bestDist)
                bestDist = dist;
            bestZTarget = el.position.z;
        }
        var startCZ = crd.position.z;
        var finalDelta = bestZTarget - pploc.position.z;
        double currentDelta = 0;
        // Note: this assuemes parentConstruct is NOT rotated
        double maxAbsSpeed = 10;
        double currentSpeed = 0;
        double accel = 15.0 * Math.Sign(finalDelta);
        var lastTime = DateTime.Now;
        await Task.Delay(100);
        logger.LogInformation("elevator delta {maxDelta}", finalDelta);
        var decel = false;
        // We are going to simulate acceleration and deceleration,
        // which helps a lot with client interpolation
        while (Math.Abs(currentDelta - finalDelta) > 0.1)
        {
            var now = DateTime.Now;
            var delta = (now-lastTime).TotalSeconds;
            lastTime = now;
            // deceleration logic
            var remain = finalDelta - currentDelta;
            double tstop = currentSpeed / accel; // double minus)
            double pstop = currentDelta + currentSpeed * tstop;// - accel * tstop*tstop;
            if ((pstop-finalDelta)*Math.Sign(finalDelta) > 0)
            {
                decel = true;
                currentSpeed -= accel * delta;
                if (Math.Sign(currentSpeed) != Math.Sign(accel))
                    break;
            }
            else if (!decel && Math.Abs(currentSpeed) < maxAbsSpeed)
                currentSpeed += accel * delta;
            currentDelta += currentSpeed * delta;
            var loc = new SimpleLocation
            {
                ConstructId = 0,
                Position = aploc.position,
            };
            logger.LogInformation("CU with delta {delta} speed {speed} pstop {pstop}", currentDelta, currentSpeed, pstop);
            var cu = new ConstructUpdate
            {
                constructId = rploc.constructId,
                baseId = crd.parentId,
                position = new NQ.Vec3{x = crd.position.x, y = crd.position.y, z = crd.position.z + currentDelta},
                rotation = crd.rotation,
                worldRelativeVelocity = new NQ.Vec3 { z  = currentSpeed},
                worldAbsoluteVelocity = new NQ.Vec3 { z  = currentSpeed},
                worldRelativeAngVelocity = new NQ.Vec3(),
                worldAbsoluteAngVelocity = new NQ.Vec3(),
                grounded = false,
                pilotId = 2,
                time = TimePoint.Now(),
            };
            var op = new NQutils.Messages.ConstructUpdate(cu);
            await isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>()
                .UpdateConstructAsync(NQutils.Serialization.Grpc.MakeEvent(new NQutils.Messages.ConstructUpdate(cu)));
            /*
            await isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>()
                .PublishGenericEventAsync(new EventLocation
                    {
                        Event = NQutils.Serialization.Grpc.MakeEvent(op),
                        Location = loc,
                        VisibilityDistance = 1000,
                    });
            */
            await Task.Delay(100);
        }
        for (int i=0; i<10; i++)
        { // send a bit more updates to avoid client interpolation issues cutting us short
            var cu = new ConstructUpdate
            {
                constructId = rploc.constructId,
                baseId = crd.parentId,
                position = new NQ.Vec3{x = crd.position.x, y = crd.position.y, z = crd.position.z + currentDelta},
                rotation = crd.rotation,
                worldRelativeVelocity = new NQ.Vec3 { z  = currentSpeed},
                worldAbsoluteVelocity = new NQ.Vec3 { z  = currentSpeed},
                worldRelativeAngVelocity = new NQ.Vec3(),
                worldAbsoluteAngVelocity = new NQ.Vec3(),
                grounded = false,
                pilotId = 2,
                time = TimePoint.Now(),
            };
            var op = new NQutils.Messages.ConstructUpdate(cu);
            await isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>()
                .UpdateConstructAsync(NQutils.Serialization.Grpc.MakeEvent(new NQutils.Messages.ConstructUpdate(cu)));
            await Task.Delay(100);
        }
    }
    public async Task SecretDoor(ulong playerId, ulong constructId)
    {
        var rploc = await scenegraph.GetPlayerLocation(playerId);
        logger.LogInformation("Player on {playerConstruct} target {targetConstruct}", rploc.constructId, constructId);
        var elts = await orleans.GetConstructElementsGrain(rploc.constructId).GetVisibleAt(0);
        var zs = new List<double>();
        var rtloc = await isp.GetRequiredService<IScenegraphAPI>().GetConstructPosition(constructId);
        var ptloc = await scenegraph.ResolveRelativeLocation(rtloc, rploc.constructId);
        var tElts = await orleans.GetConstructElementsGrain(constructId).GetVisibleAt(0);
        NQ.RelativeLocation tCoreLoc = null; 
        // locate core unit and extract it's local position
        foreach (var el in tElts.elements)
        {
            if (el.localId != 1)
                continue;
            tCoreLoc = new NQ.RelativeLocation
            {
                constructId = constructId,
                position = el.position,
                rotation = el.rotation,
            };
        }
        // resolve the core unit location in referencial of 'parent' construct, aka the one with adjustor markers
        var tCoreParentLoc = await scenegraph.ResolveRelativeLocation(tCoreLoc, rploc.constructId);
        // Find adjustors close by to act as target position markers
        foreach (var el in elts.elements)
        {
            if (el.elementType != 2648523849)
                continue;
            var v1 = new NQ.Vec3{x = tCoreParentLoc.position.x, y = tCoreParentLoc.position.y};
            var v2 = new NQ.Vec3{x = el.position.x, y = el.position.y};
            var dist = v1.Dist(v2);
            logger.LogInformation("candidate d={dist}", dist);
            if (dist < 5.0) // fixme: compute dist to core instead to have lower margin
                zs.Add(el.position.z);
        }
        if (zs.Count != 2)
        {
            logger.LogWarning("Failed to locate elevator marks {count}", zs.Count);
            return;
        }
        double znear;
        double zfar;
        if (Math.Abs(zs[0]-tCoreParentLoc.position.z) < Math.Abs(zs[1]-tCoreParentLoc.position.z))
        {
            znear = zs[0];
            zfar = zs[1];
        }
        else
        {
            znear = zs[1];
            zfar = zs[0];
        }
        // be more clever go to target
        double delta = zfar - tCoreParentLoc.position.z;
        logger.LogInformation("znear {znear} zfar {zfar} delta {delta}", znear, zfar, delta);
        
        const int steps = 20;
        double currentSpeed = delta / 2;
        ConstructUpdate cu = null;
        for (int i=0; i<steps; i++)
        {
            double factor = (i==0) ?  0.5 : 1.0;
            double stepDelta = delta / (double)steps * (double)(i+1) * factor;
            cu = new ConstructUpdate
            {
                constructId = constructId,
                baseId = rtloc.constructId,
                position = new NQ.Vec3{x = rtloc.position.x, y = rtloc.position.y, z = rtloc.position.z + stepDelta},
                rotation = rtloc.rotation,
                worldRelativeVelocity = (new NQ.Vec3 { z  = currentSpeed})*factor,
                worldAbsoluteVelocity = (new NQ.Vec3 { z  = currentSpeed})*factor,
                worldRelativeAngVelocity = new NQ.Vec3(),
                worldAbsoluteAngVelocity = new NQ.Vec3(),
                grounded = false,
                pilotId = 2,
                time = TimePoint.Now(),
            };
            await isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>()
                .UpdateConstructAsync(NQutils.Serialization.Grpc.MakeEvent(new NQutils.Messages.ConstructUpdate(cu)));
            await Task.Delay(100);
        }
        cu.worldRelativeVelocity = new NQ.Vec3();
        cu.worldAbsoluteVelocity = new NQ.Vec3();
        // continue sending updates for a bit for client interpolation to catch up
        for (int i=0; i<10; i++)
        {
            await isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>()
                .UpdateConstructAsync(NQutils.Serialization.Grpc.MakeEvent(new NQutils.Messages.ConstructUpdate(cu)));
            await Task.Delay(100);
        }
    }
    public async Task TriggerAction(ulong playerId, ModAction action)
    {
        if (action.actionId == 502)
        {
            await SecretDoor(playerId, action.constructId);
            return;
        }
        if (action.actionId == 500 || action.actionId == 501)
        {
            await Rotate(playerId, action.constructId, action.actionId == 500);
            return;
        }
        if (action.actionId == 99)
        {
            await Elevate(playerId, action.constructId);
            return;
        }
    }
}