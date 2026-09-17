using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARSpace.Core
{
    /// <summary>
    /// Lightweight service locator. Services are plain C# classes or MonoBehaviours
    /// registered once during <see cref="ARSpaceApp.Awake"/> and resolved by type.
    /// No singletons reaching into each other — everything goes through here.
    /// </summary>
    public static class ServiceLocator
    {
        static readonly Dictionary<Type, object> s_Services = new Dictionary<Type, object>();

        /// <summary>
        /// Register a service instance. Overwrites any previous registration of the same type.
        /// </summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                Debug.LogError($"[ServiceLocator] Attempted to register null for {typeof(T).Name}.");
                return;
            }

            s_Services[typeof(T)] = service;
        }

        /// <summary>
        /// Resolve a registered service. Returns null and logs an error if not found.
        /// </summary>
        public static T Get<T>() where T : class
        {
            if (s_Services.TryGetValue(typeof(T), out var service))
            {
                return service as T;
            }

            Debug.LogError($"[ServiceLocator] Service {typeof(T).Name} not registered. " +
                           "Ensure ARSpaceApp has initialised before accessing services.");
            return null;
        }

        /// <summary>
        /// Try to resolve a service without logging an error on miss.
        /// Useful during shutdown or optional dependencies.
        /// </summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (s_Services.TryGetValue(typeof(T), out var obj))
            {
                service = obj as T;
                return service != null;
            }

            service = null;
            return false;
        }

        /// <summary>
        /// Remove all registrations. Called on application quit to prevent stale references.
        /// </summary>
        public static void Clear()
        {
            s_Services.Clear();
        }
    }
}
