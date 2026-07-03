# azure-logstream-fn

## What this is

The official SlashID Azure Monitor forwarder consumes diagnostic logs from an Event Hub receiving Azure Monitor and Entra identity events and POSTs them to SlashID's `azure-monitor-logs` channel for identity analytics. This forwarder is deployed either automatically by SlashID when a connection has the required elevated grants and event streaming is enabled, or by the customer using the Deploy to Azure button below.

## Deploy to Azure

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fslashid%2Fazure-logstream-fn%2Fv0.1.0%2Fazuredeploy.json)

The button launches an Azure Resource Manager deployment that provisions the function app and event hub. You will be prompted for:

- **Resource Group**: the target resource group in your Azure subscription.
- **Function App Name**: a globally unique name for the forwarder function.
- **eventsToken**: your connection's events token, found in the SlashID console under Connection Details.

After deployment, create an **Entra ID diagnostic setting** in the Azure portal to stream logs to the created event hub:

1. Go to Azure AD → Diagnostic Settings → Add Diagnostic Setting.
2. Select **SignInLogs**, **NonInteractiveUserSignInLogs**, **ServicePrincipalSignInLogs**, and **MicrosoftGraphActivityLogs**.
3. Send to the **Event Hub** created by this deployment, in the `slashid-logs` namespace.

For guided setup, see the [SlashID Dev Portal Microsoft Onboarding guide](https://dev.slashid.com).

## App-settings contract

The following environment variables configure the forwarder and must match across this repository and `slashid/ng-evangelion` (provisioner and ARM template). **Do not rename these on one side of the contract alone.**

| Setting | Value / source |
| --- | --- |
| `EventHubConnection` | Event Hub connection string (Listen auth rule) |
| `EVENTHUB_NAME` | `slashid-logs` |
| `EVENTHUB_CONSUMER_GROUP` | `sid-fn` |
| `SLASHID_EVENTS_ENDPOINT` | SlashID `azure-monitor-logs` channel endpoint |
| `SLASHID_PUSH_AUTH_TOKEN` | the connection's events token (SlashID console → connection details) |
| `FORWARDER_VERSION` | optional; release version WITHOUT leading `v` (e.g. `0.1.0`) |

## Reliability model

The forwarder ensures no loss of diagnostic logs under normal circumstances. Checkpoint advancement is deferred until successful delivery to SlashID (`FixedDelayRetry(-1)` retries indefinitely). If SlashID is unreachable, backpressure accumulates into Event Hub retention, preventing loss. Duplicate delivery is possible on redelivery following a transient failure. The only record drop occurs when a single diagnostic log exceeds the 1 MiB receiver size limit, which is reported via a `record_dropped` control event sent to SlashID.

## Release flow

> **A release is not live anywhere until the ng-evangelion pin is bumped.**
>
> 1. **Stage:** tag `vX.Y.Z` → CI attaches `released-package.zip` (+ sha256) to a **pre-release**. Pre-releases are staged builds: publicly fetchable at their versioned URL, never `latest`, never for production.
> 2. **Validate:** deploy the staged versioned URL to the sandbox tenant; run the E2E checklist (below).
> 3. **Publish:** promote the release by clearing the pre-release flag. Publishing alone changes nothing in production — nothing follows "latest".
> 4. **Adopt:** bump the pinned constant in `slashid/ng-evangelion` (`backend/modules/nhi/adapters/infra_provisioning/microsoft/streaming_names.go`) — **only ever to a published, non-pre-release version** — and update the Deploy-to-Azure button tag in this README. Auto-provisioned forwarders converge within ~1 hour (upgrade-on-reconcile); manual-mode customers see an "outdated forwarder" notice in the SlashID console.
> 5. **Rollback:** revert the pin bump; the same reconcile converges the fleet back.

## Local development

To run and test the forwarder locally:

1. Create `local.settings.json` in the repository root with the five app-settings contract variables (see above) plus `AzureWebJobsStorage` for the local Azure Storage emulator:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "EventHubConnection": "<event-hub-connection-string>",
    "EVENTHUB_NAME": "slashid-logs",
    "EVENTHUB_CONSUMER_GROUP": "sid-fn",
    "SLASHID_EVENTS_ENDPOINT": "<azure-monitor-logs-channel-endpoint>",
    "SLASHID_PUSH_AUTH_TOKEN": "<connection-events-token>",
    "FORWARDER_VERSION": "0.1.0"
  }
}
```

2. Start the forwarder with `func start`.
3. Run the full test suite with `dotnet test`.

## Sandbox E2E checklist

Before promoting a staged release to published, validate it in the sandbox tenant using this checklist:

- [ ] Burst 10k records through the event hub and verify `records-in == records-delivered` (no loss or duplication in the receiver logs).
- [ ] Block the SlashID receiver for 15+ minutes and confirm the checkpoint is frozen; after unblocking, verify a full drain occurs with zero loss.
- [ ] Send requests with an invalid or expired `SLASHID_PUSH_AUTH_TOKEN` and verify 401 responses prevent checkpoint advancement; after fixing the token, verify the queue drains.
- [ ] Confirm the forwarder sends a heartbeat control event (type: `slashid.forwarder.heartbeat`) every ~5 minutes and reports the correct `FORWARDER_VERSION`.
- [ ] Send a single diagnostic record exceeding 768 KB and verify a `record_dropped` control event is generated; confirm all other records are still delivered.
