using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NATS.Client.Core.Tests;

public class ObjectPoolTest
{
    [Fact]
    public void ObjectPushPop()
    {
        var pool = new ObjectPool(6);

        pool.TryRent<PoolTestObject1>(out _).ShouldBeFalse();
        pool.Return(new PoolTestObject1()).ShouldBeTrue();
        pool.Return(new PoolTestObject1()).ShouldBeTrue();
        pool.Return(new PoolTestObject1()).ShouldBeTrue();
        pool.Return(new PoolTestObject1()).ShouldBeTrue();
        pool.Return(new PoolTestObject1()).ShouldBeTrue();
        pool.Return(new PoolTestObject1()).ShouldBeTrue();
        pool.Return(new PoolTestObject1()).ShouldBeFalse();

        pool.TryRent<PoolTestObject1>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject1>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject1>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject1>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject1>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject1>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject1>(out _).ShouldBeFalse();
    }

    [Fact]
    public void ManyType()
    {
        var pool = new ObjectPool(6);

        pool.TryRent<PoolTestObject1>(out _).ShouldBeFalse();
        pool.TryRent<PoolTestObject2>(out _).ShouldBeFalse();
        pool.TryRent<PoolTestObject3>(out _).ShouldBeFalse();
        pool.TryRent<PoolTestObject4>(out _).ShouldBeFalse();
        pool.TryRent<PoolTestObject5>(out _).ShouldBeFalse();

        pool.Return(new PoolTestObject1());
        pool.Return(new PoolTestObject2());
        pool.Return(new PoolTestObject3());
        pool.Return(new PoolTestObject4());
        pool.Return(new PoolTestObject5());

        pool.TryRent<PoolTestObject1>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject2>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject3>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject4>(out _).ShouldBeTrue();
        pool.TryRent<PoolTestObject5>(out _).ShouldBeTrue();
    }
}

class PoolTestObject1 : IObjectPoolNode<PoolTestObject1>
{
    PoolTestObject1? next;
    public ref PoolTestObject1? NextNode => ref next;
}


class PoolTestObject2 : IObjectPoolNode<PoolTestObject2>
{
    PoolTestObject2? next;
    public ref PoolTestObject2? NextNode => ref next;
}


class PoolTestObject3 : IObjectPoolNode<PoolTestObject3>
{
    PoolTestObject3? next;
    public ref PoolTestObject3? NextNode => ref next;
}


class PoolTestObject4 : IObjectPoolNode<PoolTestObject4>
{
    PoolTestObject4? next;
    public ref PoolTestObject4? NextNode => ref next;
}


class PoolTestObject5 : IObjectPoolNode<PoolTestObject5>
{
    PoolTestObject5? next;
    public ref PoolTestObject5? NextNode => ref next;
}
