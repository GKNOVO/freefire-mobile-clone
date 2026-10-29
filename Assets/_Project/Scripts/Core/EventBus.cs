using System;
using System.Collections.Generic;
using UnityEngine;

namespace BR.Core
{
    /// <summary>
    /// Event bus tipado e estatico, com payloads struct (sem boxing / sem GC por evento).
    /// Regra de ouro do projeto: managers NUNCA tem referencia direta de UI.
    /// Gameplay publica eventos -> UI consome.
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> Handlers = new Dictionary<Type, Delegate>(64);

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;

            var type = typeof(T);
            Handlers.TryGetValue(type, out var existing);
            Handlers[type] = Delegate.Combine(existing, handler);
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;

            var type = typeof(T);
            if (!Handlers.TryGetValue(type, out var existing)) return;

            var updated = Delegate.Remove(existing, handler);
            if (updated == null) Handlers.Remove(type);
            else Handlers[type] = updated;
        }

        public static void Publish<T>(in T evt) where T : struct
        {
            if (!Handlers.TryGetValue(typeof(T), out var d)) return;

            // Um handler com excecao nao pode derrubar o resto da cadeia.
            foreach (var callback in d.GetInvocationList())
            {
                try { ((Action<T>)callback).Invoke(evt); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Limpa handlers no Play Mode com Domain Reload desativado.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Handlers.Clear();
    }
}
