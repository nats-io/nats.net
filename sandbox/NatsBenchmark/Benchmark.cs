// Originally this benchmark code is borrowd from https://github.com/nats-io/nats.net
#nullable disable

// Copyright 2015-2018 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client;
using ZLogger;

namespace NatsBenchmark
{
    public partial class Benchmark
    {
        private static readonly long DEFAULTCOUNT = 10000000;

        private string _url = null;
        private long _count = DEFAULTCOUNT;
        private long _payloadSize = 0;
        private string _subject = "s";
        private bool _useOldRequestStyle = true;
        private string _creds = null;

        private BenchType _btype = BenchType.SUITE;

        public Benchmark(string[] args)
        {
            if (!ParseArgs(args))
                return;

            switch (_btype)
            {
                case BenchType.SUITE:
                    RunSuite();
                    break;
                case BenchType.PUB:
                    RunPub("PUB", _count, _payloadSize);
                    break;
                case BenchType.PUBSUB:
                    RunPubSub("PUBSUB", _count, _payloadSize);
                    break;
                case BenchType.REQREPLY:
                    RunReqReply("REQREP", _count, _payloadSize);
                    break;
                case BenchType.REQREPLYASYNC:
                    RunReqReplyAsync("REQREPASYNC", _count, _payloadSize).Wait();
                    break;
                default:
                    throw new Exception("Invalid Type.");
            }
        }

        private enum BenchType
        {
            PUB = 0,
            PUBSUB,
            REQREPLY,
            SUITE,
            REQREPLYASYNC,
        }

        private void SetBenchType(string value)
        {
            switch (value)
            {
                case "PUB":
                    _btype = BenchType.PUB;
                    break;
                case "PUBSUB":
                    _btype = BenchType.PUBSUB;
                    break;
                case "REQREP":
                    _btype = BenchType.REQREPLY;
                    break;
                case "REQREPASYNC":
                    _btype = BenchType.REQREPLYASYNC;
                    break;
                case "SUITE":
                    _btype = BenchType.SUITE;
                    break;
                default:
                    _btype = BenchType.PUB;
                    Console.WriteLine("No type specified.  Defaulting to PUB.");
                    break;
            }
        }

        private void Usage()
        {
            Console.WriteLine("benchmark [-h] -type <PUB|PUBSUB|REQREP|REQREPASYNC|SUITE> -url <server url> -count <test count> -creds <creds file> -size <payload size (bytes)>");
        }

        private string GetValue(IDictionary<string, string> values, string key, string defaultValue)
        {
            if (values.ContainsKey(key))
                return values[key];

            return defaultValue;
        }

        private bool ParseArgs(string[] args)
        {
            try
            {
                // defaults
                if (args == null || args.Length == 0)
                    return true;

                IDictionary<string, string> strArgs = new Dictionary<string, string>();

                for (int i = 0; i < args.Length; i++)
                {
                    if (i + 1 > args.Length)
                        throw new Exception("Missing argument after " + args[i]);

                    if ("-h".Equals(args[i].ToLower()) ||
                        "/?".Equals(args[i].ToLower()))
                    {
                        Usage();
                        return false;
                    }

                    strArgs.Add(args[i], args[i + 1]);
                    i++;
                }

                SetBenchType(GetValue(strArgs, "-type", "PUB"));

                _url = GetValue(strArgs, "-url", "nats://localhost:4222");
                _count = Convert.ToInt64(GetValue(strArgs, "-count", "10000"));
                _payloadSize = Convert.ToInt64(GetValue(strArgs, "-size", "0"));
                _useOldRequestStyle = Convert.ToBoolean(GetValue(strArgs, "-old", "false"));
                _creds = GetValue(strArgs, "-creds", null);

                Console.WriteLine("Running NATS Custom benchmark:");
                Console.WriteLine("    URL:   " + _url);
                Console.WriteLine("    Count: " + _count);
                Console.WriteLine("    Size:  " + _payloadSize);
                Console.WriteLine("    Type:  " + GetValue(strArgs, "-type", "PUB"));
                Console.WriteLine(string.Empty);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to parse command line args: " + e.Message);
                return false;
            }
        }

        private void PrintResults(string testPrefix, Stopwatch sw, long testCount, long msgSize)
        {
            int msgRate = (int)(testCount / sw.Elapsed.TotalSeconds);

            Console.WriteLine(
                "{0}\t{1,10}\t{2,10} msgs/s\t{3,8} kb/s",
                testPrefix,
                testCount,
                msgRate,
                msgRate * msgSize / 1024);
        }

        private byte[] GeneratePayload(long size)
        {
            byte[] data = null;

            if (size == 0)
                return null;

            data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)'a';
            }

            return data;
        }

        private void RunPub(string testName, long testCount, long testSize)
        {
            byte[] payload = GeneratePayload(testSize);

            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = _url;
            if (_creds != null)
            {
                opts.SetUserCredentials(_creds);
            }

            using (IConnection c = new ConnectionFactory().CreateConnection(opts))
            {
                Stopwatch sw = sw = Stopwatch.StartNew();

                for (int i = 0; i < testCount; i++)
                {
                    c.Publish(_subject, payload);
                }

                sw.Stop();

                PrintResults(testName, sw, testCount, testSize);
            }
        }

        private void RunPubSub(string testName, long testCount, long testSize)
        {
            object pubSubLock = new object();
            bool finished = false;
            int subCount = 0;

            byte[] payload = GeneratePayload(testSize);

            ConnectionFactory cf = new ConnectionFactory();

            Options o = ConnectionFactory.GetDefaultOptions();
            o.ClosedEventHandler = (_, __) => { };
            o.DisconnectedEventHandler = (_, __) => { };

            o.Url = _url;
            o.SubChannelLength = 10000000;
            if (_creds != null)
            {
                o.SetUserCredentials(_creds);
            }

            o.AsyncErrorEventHandler += (sender, obj) =>
            {
                Console.WriteLine("Error: " + obj.Error);
            };

            IConnection subConn = cf.CreateConnection(o);
            IConnection pubConn = cf.CreateConnection(o);

            IAsyncSubscription s = subConn.SubscribeAsync(_subject, (sender, args) =>
            {
                subCount++;
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });
            s.SetPendingLimits(10000000, 1000000000);
            subConn.Flush();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < testCount; i++)
            {
                pubConn.Publish(_subject, payload);
            }

            pubConn.Flush();

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);
            }

            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            pubConn.Close();
            subConn.Close();
        }

        private double ConvertTicksToMicros(long ticks)
        {
            return ConvertTicksToMicros((double)ticks);
        }

        private double ConvertTicksToMicros(double ticks)
        {
            return ticks / TimeSpan.TicksPerMillisecond * 1000.0;
        }

        private void RunPubSubLatency(string testName, long testCount, long testSize)
        {
            object subcriberLock = new object();
            bool subscriberDone = false;

            List<long> measurements = new List<long>((int)testCount);

            byte[] payload = GeneratePayload(testSize);

            ConnectionFactory cf = new ConnectionFactory();
            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = _url;
            if (_creds != null)
            {
                opts.SetUserCredentials(_creds);
            }

            IConnection subConn = cf.CreateConnection(opts);
            IConnection pubConn = cf.CreateConnection(opts);

            Stopwatch sw = new Stopwatch();

            IAsyncSubscription subs = subConn.SubscribeAsync(_subject, (sender, args) =>
            {
                sw.Stop();

                measurements.Add(sw.ElapsedTicks);

                lock (subcriberLock)
                {
                    Monitor.Pulse(subcriberLock);
                    subscriberDone = true;
                }
            });

            subConn.Flush();

            for (int i = 0; i < testCount; i++)
            {
                lock (subcriberLock)
                {
                    subscriberDone = false;
                }

                sw.Reset();
                sw.Start();

                pubConn.Publish(_subject, payload);
                pubConn.Flush();

                // block on the subscriber finishing - we do not want any
                // overlap in measurements.
                lock (subcriberLock)
                {
                    if (!subscriberDone)
                    {
                        Monitor.Wait(subcriberLock);
                    }
                }
            }

            double latencyAvg = measurements.Average();

            double stddev = Math.Sqrt(
                measurements.Average(
                    v => Math.Pow(v - latencyAvg, 2)));

            Console.WriteLine(
                "{0} (us)\t{1} msgs, {2:F2} avg, {3:F2} min, {4:F2} max, {5:F2} stddev",
                testName,
                testCount,
                ConvertTicksToMicros(latencyAvg),
                ConvertTicksToMicros(measurements.Min()),
                ConvertTicksToMicros(measurements.Max()),
                ConvertTicksToMicros(stddev));

            pubConn.Close();
            subConn.Close();
        }

        private void RunReqReply(string testName, long testCount, long testSize)
        {
            byte[] payload = GeneratePayload(testSize);

            ConnectionFactory cf = new ConnectionFactory();

            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = _url;
            opts.UseOldRequestStyle = _useOldRequestStyle;
            if (_creds != null)
            {
                opts.SetUserCredentials(_creds);
            }

            IConnection subConn = cf.CreateConnection(opts);
            IConnection pubConn = cf.CreateConnection(opts);

            Thread t = new Thread(() =>
            {
                ISyncSubscription s = subConn.SubscribeSync(_subject);
                for (int i = 0; i < testCount; i++)
                {
                    Msg m = s.NextMessage();
                    subConn.Publish(m.Reply, payload);
                    subConn.Flush();
                }
            });
            t.IsBackground = true;
            t.Start();

            Thread.Sleep(1000);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < testCount; i++)
            {
                pubConn.Request(_subject, payload);
            }

            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            pubConn.Close();
            subConn.Close();
        }

        private async Task RunReqReplyAsync(string testName, long testCount, long testSize)
        {
            byte[] payload = GeneratePayload(testSize);

            ConnectionFactory cf = new ConnectionFactory();

            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = _url;
            opts.UseOldRequestStyle = _useOldRequestStyle;
            if (_creds != null)
            {
                opts.SetUserCredentials(_creds);
            }

            IConnection subConn = cf.CreateConnection(opts);
            IConnection pubConn = cf.CreateConnection(opts);

            Thread t = new Thread(() =>
            {
                ISyncSubscription s = subConn.SubscribeSync(_subject);
                for (int i = 0; i < testCount; i++)
                {
                    Msg m = s.NextMessage();
                    subConn.Publish(m.Reply, payload);
                    subConn.Flush();
                }
            });
            t.IsBackground = true;
            t.Start();

            Thread.Sleep(1000);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < testCount; i++)
            {
                await pubConn.RequestAsync(_subject, payload).ConfigureAwait(false);
            }

            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            pubConn.Close();
            subConn.Close();
        }
    }
}
