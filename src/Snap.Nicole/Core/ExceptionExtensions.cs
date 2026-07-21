using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Snap.Nicole.Core;

internal static class ExceptionExtensions
{
    extension(ArgumentException)
    {
        public static void ThrowIf(bool condition, [ConstantExpected] string message, string? paramName)
        {
            if (condition)
            {
                throw new ArgumentException(message, paramName);
            }
        }

        public static void ThrowIfNot(bool condition, [ConstantExpected] string message, string? paramName)
        {
            if (!condition)
            {
                throw new ArgumentException(message, paramName);
            }
        }

        public static void ThrowIfEmpty<T>(IReadOnlyCollection<T> argument, [ConstantExpected] string message, string? paramName)
        {
            if (argument.Count is 0)
            {
                throw new ArgumentException(message, paramName);
            }
        }

        public static void ThrowIfNullOrEmpty(string? argument, [ConstantExpected] string message, string? paramName)
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

        public static void ThrowIfNullOrWhiteSpace(string? argument, [ConstantExpected] string message, string? paramName)
        {
            if (string.IsNullOrWhiteSpace(argument))
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
        public static void ThrowIf(bool condition, [ConstantExpected] string message)
        {
            if (condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void ThrowIfNot(bool condition, [ConstantExpected] string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}