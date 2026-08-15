using System;
using System.IO.Pipes;
using System.Threading.Tasks;
using GameLock.Common;

namespace GameLock.Gui
{
    /// <summary>Thin client wrapper for talking to the GameLockService over the named pipe.</summary>
    public static class PipeClient
    {
        private const int ConnectTimeoutMs = 3000;

        public static async Task<PipeResponse> SendAsync(PipeRequest request)
        {
            using var client = new NamedPipeClientStream(".", PipeChannel.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await client.ConnectAsync(ConnectTimeoutMs);
            }
            catch (TimeoutException)
            {
                return new PipeResponse { Success = false, Message = "GameLock protection service is not running. Use 'Install Protection' first." };
            }
            catch (Exception ex)
            {
                return new PipeResponse { Success = false, Message = $"Could not reach protection service: {ex.Message}" };
            }

            await PipeChannel.WriteMessageAsync(client, request);
            var response = await PipeChannel.ReadMessageAsync<PipeResponse>(client);
            return response ?? new PipeResponse { Success = false, Message = "No response from service." };
        }
    }
}
