using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Models;
using OpenAI;
using OpenAI.Models;
using Sentry;
using Snap.Nicole.Core;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Services.AI.Models;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI;

internal sealed class ModelProfileService : IModelProfileService
{
    public Task<IReadOnlyList<ModelProfile>> GetModelsAsync(ModelProviderProfile providerProfile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerProfile);
        return GetModelsCoreAsync(providerProfile, cancellationToken);
    }

    private static async Task<IReadOnlyList<ModelProfile>> GetModelsCoreAsync(ModelProviderProfile providerProfile, CancellationToken cancellationToken)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsModelProfilesFetch, "Fetch model list");
        span.SetTag(SentryTags.AIProvider, providerProfile.ProviderType.Value.ToString());

        try
        {
            IReadOnlyList<ModelProfile> result = providerProfile.ProviderType.Value switch
            {
                ModelProviderType.OpenAIChatCompletion or ModelProviderType.OpenAIResponses => await GetOpenAIModelsAsync(providerProfile, cancellationToken),
                ModelProviderType.Anthropic => await GetAnthropicModelsAsync(providerProfile, cancellationToken),
                _ => throw new NotSupportedException($"Unsupported model provider type: {providerProfile.ProviderType.Value}"),
            };

            span.SetData(SentryData.AIModelCount, result.Count);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            span.Finish(ex);
            throw;
        }
    }

    private static async Task<IReadOnlyList<ModelProfile>> GetOpenAIModelsAsync(ModelProviderProfile providerProfile, CancellationToken cancellationToken)
    {
        ApiKeyCredential apiKeyCredential = new(providerProfile.ApiKey!);
        OpenAIClientOptions options = new()
        {
            Endpoint = providerProfile.Endpoint.ToUri(),
        };

        OpenAIModelClient client = new OpenAIClient(apiKeyCredential, options).GetOpenAIModelClient();
        ClientResult<OpenAIModelCollection> result = await client.GetModelsAsync(cancellationToken);

        IEnumerable<ModelProfile> models = result.Value
            .Where(static model => !string.IsNullOrWhiteSpace(model.Id))
            .Where(static model =>
            {
                IDictionary<string, BinaryData> additionalRawData = GetSerializedAdditionalRawData(model);
                if (additionalRawData.TryGetValue("status", out BinaryData? statusData))
                {
                    string? status = statusData.ToObjectFromJson<string>();
                    if (status is "Shutdown")
                    {
                        return false;
                    }
                }

                return true;
            })
            .Select(ModelProfile.Create);
        return [.. models];
    }

    private static async Task<IReadOnlyList<ModelProfile>> GetAnthropicModelsAsync(ModelProviderProfile providerProfile, CancellationToken cancellationToken)
    {
        ClientOptions clientOptions = new()
        {
            ApiKey = providerProfile.ApiKey,
            BaseUrl = string.IsNullOrWhiteSpace(providerProfile.Endpoint) ? EnvironmentUrl.Production : providerProfile.Endpoint,
        };

        using (AnthropicClient client = new(clientOptions))
        {
            List<ModelProfile> result = [];
            ModelListParams @params = new()
            {
                Limit = 1000,
            };

            ModelListPage page = await client.Models.List(@params, cancellationToken);
            await foreach(ModelInfo model in new ModelListPageAdapter(page).Paginate(cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(model.ID))
                {
                    continue;
                }

                result.Add(ModelProfile.Create(model));
            }

            return result;
        }
    }

    // internal IDictionary<string, BinaryData> SerializedAdditionalRawData
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_SerializedAdditionalRawData")]
    private static extern IDictionary<string, BinaryData> GetSerializedAdditionalRawData(OpenAIModel model);

    private sealed class ModelListPageAdapter(ModelListPage page) : IPage<ModelInfo>
    {
        private readonly ModelListPage page = page;

        public IReadOnlyList<ModelInfo> Items { get => page.Items; }

        public bool HasNext()
        {
            return page.HasNext() && GetResponse(page).HasMore;
        }

        public Task<IPage<ModelInfo>> Next(CancellationToken cancellationToken = default)
        {
            return ((IPage<ModelInfo>)page).Next(cancellationToken);
        }

        public void Validate()
        {
            page.Validate();
        }

        // private ModelListPageResponse <response>P
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<response>P")]
        private static extern ref ModelListPageResponse GetResponse(ModelListPage page);
    }
}
