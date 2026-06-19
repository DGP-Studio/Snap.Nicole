using System.Collections.Generic;

namespace Snap.Nicole.Core;

internal static class ExceptionExtensions
{
    extension(ArgumentException)
    {
        public static void ThrowIf(bool condition, string message, string? paramName)
        {
            if (condition)
            {
                throw new ArgumentException(message, paramName);
            }
        }

        public static void ThrowIfNot(bool condition, string message, string? paramName)
        {
            if (!condition)
            {
                throw new ArgumentException(message, paramName);
            }
        }

        public static void ThrowIfEmpty<T>(IReadOnlyCollection<T> argument, string message, string? paramName)
        {
            if (argument.Count is 0)
            {
                throw new ArgumentException(message, paramName);
            }
        }

        public static void ThrowIfNullOrEmpty(string? argument, string message, string? paramName)
        {
            if (string.IsNullOrEmpty(argument))
            {
                if (argument is null)
                {
                    throw new ArgumentNullException(message, default(Exception));
                }

                throw new ArgumentException(message, paramName);
            }
        }
    }

    extension(InvalidOperationException)
    {
        public static void ThrowIf(bool condition, string message)
        {
            if (condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void ThrowIfNot(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}