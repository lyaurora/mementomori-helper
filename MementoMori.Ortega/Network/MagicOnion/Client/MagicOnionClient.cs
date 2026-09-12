using System.Runtime.CompilerServices;
using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;
using MementoMori.Ortega.Common.Enums;
using MementoMori.Ortega.Network.MagicOnion.Interface;

namespace MementoMori.Ortega.Network.MagicOnion.Client
{
	public abstract class MagicOnionClient<TSender, TReceiver> : BaseMagicOnionClient where TSender : IStreamingHub<TSender, TReceiver>
	{
		public MagicOnionClient(GrpcChannel channel, long playerId, string authToken): base(playerId, authToken)
        {
            this._channel = channel;
        }

		public override bool IsExistHubClient()
        {
            return _sender != null;
        }

        private CancellationTokenSource _connectionCancellation;

        public override async Task DisposeAsync()
        {
            _connectionCancellation?.Cancel();
            try
            {
                if (_sender != null) await _sender.DisposeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                _sender = default;
                _connectionCancellation?.Dispose();
                _connectionCancellation = null;
                ChangeState(HubClientState.Disconnected);
            }
        }

		protected void AttachInternalReceiver(TReceiver internalReceiver, IDisconnectReceiver internalDisconnectReceiver)
		{
			_internalReceiver = internalReceiver;
            _internalDisconnectReceiver = internalDisconnectReceiver;
		}

		protected override async Task ConnectHub(CancellationToken cancellationToken = default)
        {
            ChangeState(HubClientState.Connecting);
            _connectionCancellation?.Cancel();
            _connectionCancellation?.Dispose();
            _connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _connectionCancellation.CancelAfter(TimeSpan.FromMinutes(1));
            _sender = await StreamingHubClient.ConnectAsync<TSender, TReceiver>(_channel, _internalReceiver,
                option: new Grpc.Core.CallOptions(cancellationToken: _connectionCancellation.Token), cancellationToken: _connectionCancellation.Token);
        }

		protected override Task Authenticate()
		{
            base.ChangeState(HubClientState.Authenticating);
            return Task.CompletedTask;
		}

		protected override void SucceededAuthentication()
		{
            _connectionCancellation?.CancelAfter(Timeout.InfiniteTimeSpan);
			base.ResetRetryCount();
			base.ChangeState(HubClientState.Ready);
		}

		protected override void FailedAuthentication()
		{
			base.ChangeState(HubClientState.FailedAuthentication);
		}

		protected override void WatchDisconnect()
		{
            _sender.WaitForDisconnect().ConfigureAwait(false).GetAwaiter().GetResult();
		}

		protected GrpcChannel _channel;

		protected TSender _sender;

		protected TReceiver _internalReceiver;

		protected IDisconnectReceiver _internalDisconnectReceiver;
	}
}
