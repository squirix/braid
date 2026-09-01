using Xunit.Sdk;

namespace Braid.Tests;

/// <summary>Provides closure-free exception assertions for braid tests.</summary>
/// <remarks>
///     <para>
///     These helpers accept an operation directly instead of wrapping it in a delegate passed to an
///     <c language="csharp">Assert.Throws</c>-style API, so sync call sites can pass a capture-free static lambda and
///     async call sites can pass an already-started <see cref="Task" /> or <see cref="ValueTask" />. This avoids the
///     display-class allocation a captured delegate would introduce.
///     </para>
///     <para>
///     Assertions on operations that throw synchronously (for example, argument validation in a non-async method body)
///     must use the synchronous overloads below; the asynchronous overloads only observe faults captured by the
///     awaitable itself.
///     </para>
/// </remarks>
public static class Assertions
{
    /// <summary>Invokes an operation and asserts it throws exactly <typeparamref name="TException" />.</summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <param name="operation">Operation expected to throw.</param>
    /// <returns>The observed exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="XunitException">Thrown when the operation does not throw exactly <typeparamref name="TException" />.</exception>
    public static TException Expects<TException>(Action operation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            operation();
        }
        catch (Exception thrown)
        {
            if (thrown is TException expected && expected.GetType() == typeof(TException))
                return expected;

            throw Unexpected<TException>(thrown);
        }

        throw Missing<TException>();
    }

    /// <summary>Invokes an operation with one state value and asserts it throws exactly <typeparamref name="TException" />.</summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <typeparam name="TState">Operation state type.</typeparam>
    /// <param name="state">State passed to <paramref name="operation" />.</param>
    /// <param name="operation">Operation expected to throw.</param>
    /// <returns>The observed exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="XunitException">Thrown when the operation does not throw exactly <typeparamref name="TException" />.</exception>
    public static TException Expects<TException, TState>(TState state, Action<TState> operation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            operation(state);
        }
        catch (Exception thrown)
        {
            if (thrown is TException expected && expected.GetType() == typeof(TException))
                return expected;

            throw Unexpected<TException>(thrown);
        }

        throw Missing<TException>();
    }

    /// <summary>Invokes an operation with two state values and asserts it throws exactly <typeparamref name="TException" />.</summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <typeparam name="TState1">First operation state type.</typeparam>
    /// <typeparam name="TState2">Second operation state type.</typeparam>
    /// <param name="state1">First state value.</param>
    /// <param name="state2">Second state value.</param>
    /// <param name="operation">Operation expected to throw.</param>
    /// <returns>The observed exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="XunitException">Thrown when the operation does not throw exactly <typeparamref name="TException" />.</exception>
    public static TException Expects<TException, TState1, TState2>(TState1 state1, TState2 state2, Action<TState1, TState2> operation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            operation(state1, state2);
        }
        catch (Exception thrown)
        {
            if (thrown is TException expected && expected.GetType() == typeof(TException))
                return expected;

            throw Unexpected<TException>(thrown);
        }

        throw Missing<TException>();
    }

    /// <summary>Invokes an operation with one state value and asserts it throws <typeparamref name="TException" /> or a derived type.</summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <typeparam name="TState">Operation state type.</typeparam>
    /// <param name="state">State passed to <paramref name="operation" />.</param>
    /// <param name="operation">Operation expected to throw.</param>
    /// <returns>The observed exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="XunitException">Thrown when the operation completes without throwing.</exception>
    public static TException ExpectsAny<TException, TState>(TState state, Action<TState> operation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            operation(state);
        }
        catch (TException thrown)
        {
            return thrown;
        }

        throw Missing<TException>();
    }

    /// <summary>Awaits an in-flight operation and asserts its faults with <typeparamref name="TException" /> or a derived type.</summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <param name="startOperation">Factory that starts and returns the operation expected to fault.</param>
    /// <returns>The observed exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startOperation" /> is <see langword="null" />.</exception>
    /// <exception cref="XunitException">Thrown when the operation completes without faulting.</exception>
    public static Task<TException> ExpectsAnyAsync<TException>(Func<ValueTask> startOperation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(startOperation);
        return ExpectsAnyAsyncCoreAsync<TException>(startOperation);
    }

    /// <summary>Faults when an in-flight operation, started via <paramref name="startOperation" /> with <paramref name="state" />, completes without throwing <typeparamref name="TException" /> or a derived type.</summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <typeparam name="TState">State type supplied to <paramref name="startOperation" />.</typeparam>
    /// <param name="state">State value supplied to <paramref name="startOperation" />.</param>
    /// <param name="startOperation">Starts the faulting task given <paramref name="state" />.</param>
    /// <returns>The observed exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startOperation" /> is <see langword="null" />.</exception>
    /// <exception cref="XunitException">Thrown when the operation completes without faulting.</exception>
    public static Task<TException> ExpectsAnyAsync<TException, TState>(TState state, Func<TState, Task> startOperation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(startOperation);
        return ExpectsAnyAsyncCoreAsync<TException, TState>(state, startOperation);
    }

    /// <summary>Awaits an in-flight operation and asserts its faults with exactly <typeparamref name="TException" />.</summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <param name="operation">The in-flight operation expected to fault.</param>
    /// <returns>The observed exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="XunitException">Thrown when the operation completes without faulting exactly with <typeparamref name="TException" />.</exception>
    public static Task<TException> ExpectsAsync<TException>(Task operation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);
        return AwaitAsync<TException>(operation, true);
    }

    /// <summary>Faults when an operation, started via <paramref name="startOperation" /> with <paramref name="state" />, completes without throwing exactly <typeparamref name="TException" />. A synchronous throw from the operation surfaces as a fault.</summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <typeparam name="TState">State type supplied to <paramref name="startOperation" />.</typeparam>
    /// <param name="state">State value supplied to <paramref name="startOperation" />.</param>
    /// <param name="startOperation">Starts the operation expected to fault given <paramref name="state" />.</param>
    /// <returns>The observed exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startOperation" /> is <see langword="null" />.</exception>
    /// <exception cref="XunitException">Thrown when the operation completes without faulting exactly with <typeparamref name="TException" />.</exception>
    public static Task<TException> ExpectsAsync<TException, TState>(TState state, Func<TState, Task> startOperation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(startOperation);
        return ExpectsAsyncCoreAsync<TException, TState>(state, startOperation);
    }

    private static async Task<TException> ExpectsAsyncCoreAsync<TException, TState>(TState state, Func<TState, Task> startOperation)
        where TException : Exception
    {
        try
        {
            await startOperation(state).ConfigureAwait(false);
        }
        catch (Exception thrown)
        {
            if (thrown is TException expected && expected.GetType() == typeof(TException))
                return expected;

            throw Unexpected<TException>(thrown);
        }

        throw Missing<TException>();
    }

    private static async Task<TException> AwaitAsync<TException>(Task operation, bool exactType)
        where TException : Exception
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception thrown)
        {
            if (thrown is TException expected && (!exactType || expected.GetType() == typeof(TException)))
                return expected;

            throw Unexpected<TException>(thrown);
        }

        throw Missing<TException>();
    }

    private static async Task<TException> ExpectsAnyAsyncCoreAsync<TException>(Func<ValueTask> startOperation)
        where TException : Exception
    {
        try
        {
            await startOperation().ConfigureAwait(false);
        }
        catch (TException thrown)
        {
            return thrown;
        }

        throw Missing<TException>();
    }

    private static async Task<TException> ExpectsAnyAsyncCoreAsync<TException, TState>(TState state, Func<TState, Task> startOperation)
        where TException : Exception
    {
        try
        {
            await startOperation(state).ConfigureAwait(false);
        }
        catch (TException thrown)
        {
            return thrown;
        }

        throw Missing<TException>();
    }

    private static XunitException Missing<TException>()
        where TException : Exception => new($"Expected exception of type {typeof(TException).FullName} but none was thrown.");

    private static XunitException Unexpected<TException>(Exception thrown)
        where TException : Exception => new($"Expected exception of type {typeof(TException).FullName} but a different exception was thrown: {thrown}");
}
