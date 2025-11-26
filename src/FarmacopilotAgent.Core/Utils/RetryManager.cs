using System;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using Serilog;

namespace FarmacopilotAgent.Core.Utils
{
    public class RetryManager
    {
        private readonly ILogger _logger;
        private readonly IAsyncPolicy _retryPolicy;
        private readonly IAsyncPolicy _dbRetryPolicy;
        private readonly IAsyncPolicy _uploadRetryPolicy;

        public RetryManager(ILogger logger)
        {
            _logger = logger;

            // Política general con exponential backoff
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retry, ctx) =>
                    {
                        _logger.Warning("Reintento {Retry} después de {Delay}ms. Error: {Error}", 
                            retry, timeSpan.TotalMilliseconds, exception.Message);
                    });

            // Política específica para base de datos con circuit breaker
            // Política específica para base de datos (sin circuit breaker para simplificar)
            _dbRetryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 5,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retry, ctx) =>
                    {
                        _logger.Warning("Reintento DB {Retry} después de {Delay}ms. Error: {Error}", 
                            retry, timeSpan.TotalMilliseconds, exception.Message);
                    });

            // Política para uploads con timeouts más largos
            _uploadRetryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 5,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
                    onRetry: (exception, timeSpan, retry, ctx) =>
                    {
                        _logger.Warning("Reintento upload {Retry} después de {Delay}s", 
                            retry, timeSpan.TotalSeconds);
                    });
        }

        public Task<T> ExecuteAsync<T>(Func<Task<T>> action) 
            => _retryPolicy.ExecuteAsync(action);

        public Task<T> ExecuteDbAsync<T>(Func<Task<T>> action) 
            => _dbRetryPolicy.ExecuteAsync(action);

        public Task<T> ExecuteUploadAsync<T>(Func<Task<T>> action) 
            => _uploadRetryPolicy.ExecuteAsync(action);

        public Task ExecuteAsync(Func<Task> action) 
            => _retryPolicy.ExecuteAsync(action);
    }
}
