using Microsoft.Extensions.AI;

namespace Snap.Nicole.Services.AI;

internal static class AIFunctionExtensions
{
    extension(AIFunction function)
    {
        public ApprovalRequiredAIFunction AsApprovalRequired()
        {
            if (function is ApprovalRequiredAIFunction approvalRequired)
            {
                return approvalRequired;
            }

            return new ApprovalRequiredAIFunction(function);
        }
    }
}