using System.Collections.Concurrent;
using System.Diagnostics;

namespace SimpleRateLimiter
{
    public class RequestLimiter
    {
        internal ConcurrentDictionary<DateTime, uint> RequestLimitBucket = new ConcurrentDictionary<DateTime, uint>();

        internal SemaphoreSlim ConcurrentTasks;

        public readonly int RateLimitPerSecond = 25;
        public readonly int RateLimitPer10Seconds = 250;

        public RequestLimiter(int rateLimitPerSecond = 25, int maxRequestPer10Seconds = 250)
        {
            RateLimitPerSecond = rateLimitPerSecond;
            RateLimitPer10Seconds = maxRequestPer10Seconds;
            ConcurrentTasks = new SemaphoreSlim(rateLimitPerSecond);
        }

        public async Task<T?> WaitAndRunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"Task: Checking rate limit");
            DateTime startWait = DateTime.Now;
            if (!await ThrottleRequests(cancellationToken))
                return default;

            Debug.WriteLine($"Task: Allowed by rate limit, waiting for semaphore");

            await ConcurrentTasks.WaitAsync(cancellationToken);
            try
            {
                TimeSpan waitTime = DateTime.Now - startWait;
                Debug.WriteLine($"Task: Executing after {waitTime.TotalMilliseconds}ms in queue");

                return await action();
            }
            finally
            {
                var allowedTasks = ConcurrentTasks.Release();
                Debug.WriteLine($"Task: Done, releasing semaphore, busy slots {allowedTasks}");
            }
        }

        internal DateTime GetTimeStamp()
        {
            var timeStamp = DateTime.Now;
            timeStamp = timeStamp.AddTicks(-(timeStamp.Ticks % TimeSpan.TicksPerSecond));
            return timeStamp;
        }

        internal uint GetCurrentRequests()
        {
            var timeStamp = GetTimeStamp();
            lock (RequestLimitBucket)
            {
                return RequestLimitBucket.ContainsKey(timeStamp) ? RequestLimitBucket[timeStamp] : 0;
            }
        }

        internal uint IncreaseCurrentRequests()
        {
            var timeStamp = GetTimeStamp();
            lock (RequestLimitBucket)
            {
                RequestLimitBucket.TryAdd(timeStamp, 0);
                return RequestLimitBucket[timeStamp]++;
            }
        }

        /// <summary>Returns true if it's allowed, returns false if the request should be cancelled</summary>
        internal async Task<bool> ThrottleRequests(CancellationToken cancellationToken)
        {
            var currentRequests = GetCurrentRequests();
            while (currentRequests >= RateLimitPerSecond)
            {
                await Task.Delay(10);
                currentRequests = GetCurrentRequests();
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }

            IncreaseCurrentRequests();

            return true;
        }
    }
}