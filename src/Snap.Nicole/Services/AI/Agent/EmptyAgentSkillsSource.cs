using Microsoft.Agents.AI;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent;

internal sealed class EmptyAgentSkillsSource : AgentSkillsSource
{
    public static EmptyAgentSkillsSource Instance { get; } = new();

    private EmptyAgentSkillsSource()
    {
    }

    public override Task<IList<AgentSkill>> GetSkillsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IList<AgentSkill>>(Array.Empty<AgentSkill>());
    }
}
