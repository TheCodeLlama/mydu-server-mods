using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Orleans.Runtime;

namespace NQ.Grains.Core;

public enum HookMode
{
    Replace, //< Replace the original method entirely
    PreCall, //< Call hook before calling the original method
    PostCall, //< Call hook with result value after calling the original method
}

// Treat this as an opaque handle
public class HookHandle
{
    public string target;
    public HookMode mode;
    public ulong id;
}

public class HookInterceptor
{
    public MethodInfo Method;
    public object Object;
    public ulong id;
}

public class Interceptors
{
    public List<HookInterceptor> pre = new();
    public List<HookInterceptor> post = new();
    public HookInterceptor repl;
}

/** The IHookCallManager is registered as a singleton and can be obtained from
    an IServiceProvider.
 */
public interface IHookCallManager
{
    /* Register an orleans method hook
       @param hookTarget "classname.methodname", classname is usually the interface
         name minus the first 'I'. Example: "PlayerGrain.GetWallet"
       @param mode how to hook:
         PreCall will call your hook with arguments (grainKeyAsString, args...) and
           then proceed to call the original method
         Replace will call your hook with arguments (ctx, args...) and not call
           the original method, ctx being a IIncomingGrainCallContext instance
         PostCall will call your hook with arguments (grainKeyAsTring, result)
           and return what this hook returns to the caller
       @param callInstance target object to call
       @param callMethod method name on 'callInstance' to call, must not be overloaded and return Task<T>(Replace, Post) or Task(Pre)
       @return a handle for unregistration
    */
    HookHandle Register(string hookTarget, HookMode mode, object callInstance, string callMethod);
    void Unregister(HookHandle handle);

    Interceptors Acquire(string hookTarget);
}