using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt.Common;

namespace LanguageExt.Thunks
{
    /// <summary>
    /// Lazily evaluates an asynchronous function and then memoizes the value
    /// Runs at most once
    /// </summary>
    public class Thunk<Env, A>
    {
        internal readonly Func<Env, Fin<A>> fun;
        internal volatile int state;
        internal Error error;
        internal A value;

        /// <summary>
        /// Construct a lazy thunk
        /// </summary>
        [Pure, MethodImpl(Thunk.mops)]
        public static Thunk<Env, A> Lazy(Func<Env, Fin<A>> fun) =>
            new Thunk<Env, A>(fun);

        /// <summary>
        /// Construct an error Thunk
        /// </summary>
        [Pure, MethodImpl(Thunk.mops)]
        public static Thunk<Env, A> Fail(Error error) =>
            new Thunk<Env, A>(Thunk.IsFailed, error);

        /// <summary>
        /// Construct a success thunk
        /// </summary>
        [Pure, MethodImpl(Thunk.mops)]
        public static Thunk<Env, A> Success(A value) =>
            new Thunk<Env, A>(value);

        /// <summary>
        /// Construct a cancelled thunk
        /// </summary>
        [Pure, MethodImpl(Thunk.mops)]
        public static Thunk<Env, A> Cancelled() =>
            new Thunk<Env, A>(Thunk.IsCancelled, Error.New(Thunk.CancelledText));

        /// <summary>
        /// Success ctor
        /// </summary>
        [MethodImpl(Thunk.mops)]
        Thunk(A value) =>
            (this.state, this.value) = (Thunk.IsSuccess, value);

        /// <summary>
        /// Failed / Cancelled constructor
        /// </summary>
        [MethodImpl(Thunk.mops)]
        Thunk(int state, Error error) =>
            (this.state, this.error) = (state, error);

        /// <summary>
        /// Lazy constructor
        /// </summary>
        [MethodImpl(Thunk.mops)]
        Thunk(Func<Env, Fin<A>> fun) =>
            this.fun = fun ?? throw new ArgumentNullException(nameof(value));

        /// <summary>
        /// Value accessor
        /// </summary>
        [Pure, MethodImpl(Thunk.mops)]
        public Fin<A> Value(Env env) =>
            Eval(env ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>
        /// Clone the thunk
        /// </summary>
        /// <remarks>For thunks that were created as pre-failed/pre-cancelled values (i.e. no delegate to run, just
        /// in a pure error state), then the clone will copy that state exactly.  For thunks that have been evaluated
        /// then a cloned thunk will reset the thunk to a non-evaluated state.  This also means any thunk that has been
        /// evaluated and failed would lose the failed status</remarks>
        /// <returns></returns>
        [Pure, MethodImpl(Thunk.mops)]
        public Thunk<Env, A> Clone() =>
            fun == null
                ? new Thunk<Env, A>(state, error)
                : new Thunk<Env, A>(fun);

        /// <summary>
        /// Functor map
        /// </summary>
        [Pure, MethodImpl(Thunk.mops)]
        public Thunk<Env, B> Map<B>(Func<A, B> f)
        {
            try
            {
                while (true)
                {
                    SpinIfEvaluating();

                    switch (state)
                    {
                        case Thunk.IsSuccess:
                            return Thunk<Env, B>.Success(f(value));

                        case Thunk.NotEvaluated:
                            return Thunk<Env, B>.Lazy(e =>
                            {
                                var ev = fun(e);
                                if (ev.IsSucc)
                                {
                                    return Fin<B>.Succ(f(ev.value));
                                }
                                else
                                {
                                    return ev.Cast<B>();
                                }
                            });

                        case Thunk.IsCancelled:
                            return Thunk<Env, B>.Cancelled();

                        case Thunk.IsFailed:
                            return Thunk<Env, B>.Fail(error);
                    }
                }
            }
            catch (Exception e)
            {
                return Thunk<Env, B>.Fail(e);
            }
        }
        
        
        /// <summary>
        /// Functor map
        /// </summary>
        [Pure, MethodImpl(Thunk.mops)]
        public Thunk<Env, B> BiMap<B>(Func<A, B> Succ, Func<Error, Error> Fail)
        {
            try
            {
                while (true)
                {
                    SpinIfEvaluating();

                    switch (state)
                    {
                        case Thunk.IsSuccess:
                            return Thunk<Env, B>.Success(Succ(value));

                        case Thunk.NotEvaluated:
                            return Thunk<Env, B>.Lazy(e =>
                            {
                                var ev = fun(e);
                                return ev.IsSucc
                                    ? Fin<B>.Succ(Succ(ev.value))
                                    : Fin<B>.Fail(Fail(ev.Error));
                            });

                        case Thunk.IsCancelled:
                            return Thunk<Env, B>.Fail(Fail(Error.New(Thunk.CancelledText)));

                        case Thunk.IsFailed:
                            return Thunk<Env, B>.Fail(Fail(error));
                    }
                }
            }
            catch (Exception e)
            {
                return Thunk<Env, B>.Fail(Fail(e));
            }
        }

        /// <summary>
        /// Evaluates the lazy function if necessary, returns the result if it's previously been evaluated
        /// The thread goes into a spin-loop if more than one thread tries to access the lazy value at once.
        /// This is to protect against multiple evaluations.  This technique allows for a lock free access
        /// the vast majority of the time.
        /// </summary>
        [Pure]
        Fin<A> Eval(Env env)
        {
            while (true)
            {
                if (Interlocked.CompareExchange(ref state, Thunk.Evaluating, Thunk.NotEvaluated) == Thunk.NotEvaluated)
                {
                    try
                    {
                        var v = fun(env);
                        if (v.IsFail)
                        {
                            error = v.Error;
                            state = Thunk.IsFailed; // state update must be last thing before return
                            return v;
                        }
                        else
                        {
                            value = v.Value;
                            state = Thunk.IsSuccess; // state update must be last thing before return
                            return v;
                        }
                    }
                    catch (Exception e)
                    {
                        error = e;
                        state = e.Message == Thunk.CancelledText // state update must be last thing before return
                            ? Thunk.IsCancelled
                            : Thunk.IsFailed; 
                        return Fin<A>.Fail(Error.New(e));
                    }
                }
                else
                {
                    SpinIfEvaluating();

                    // Once we're here we should have a result from the eval thread and
                    // so we can use `value` to return
                    switch (state)
                    {
                        case Thunk.NotEvaluated: 
                        case Thunk.Evaluating: 
                            continue;
                        case Thunk.IsSuccess: 
                            return Fin<A>.Succ(value);
                        case Thunk.IsCancelled: 
                            return Fin<A>.Fail(Error.New(Thunk.CancelledText));
                        case Thunk.IsFailed:
                            return Fin<A>.Fail(error);
                        default:
                            throw new InvalidOperationException("should never happen");
                    }
                }
            }
        }
        
        /// <summary>
        /// Spin whilst it's running so we don't run the operation twice
        /// this will block obviously, but this event should be super rare
        /// and it's purely to stop race conditions with the eval
        /// </summary>
        [MethodImpl(Thunk.mops)]
        public void SpinIfEvaluating()
        {
            while (state == Thunk.Evaluating)
            {
                SpinWait sw = default;
                sw.SpinOnce();
            }
        }
 
        [Pure, MethodImpl(Thunk.mops)]
        public override string ToString() =>
            state switch
            {
                Thunk.NotEvaluated => "Not evaluated",
                Thunk.Evaluating => "Evaluating",
                Thunk.IsSuccess => $"Success({value})",
                Thunk.IsCancelled => $"Cancelled",
                Thunk.IsFailed => $"Failed({error})",
                _ => ""
            };
    }
}
