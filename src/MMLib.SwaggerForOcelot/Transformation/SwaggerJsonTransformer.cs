using MMLib.SwaggerForOcelot.Configuration;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MMLib.SwaggerForOcelot.Transformation
{
    /// <summary>
    /// Class which implement transformation downstream service swagger json into upstream format
    /// </summary>
    /// <seealso cref="MMLib.SwaggerForOcelot.Transformation.ISwaggerJsonTransformer" />
    public class SwaggerJsonTransformer : ISwaggerJsonTransformer
    {

        /// <inheritdoc/>
        public string Transform(string swaggerJson, IEnumerable<ReRouteOptions> reRoutes)
        {
            JObject swagger = JObject.Parse(swaggerJson);
            var paths = swagger[SwaggerProperties.Paths];

            RemoveHost(swagger);

            if (paths != null)
            {
                RemovePaths(reRoutes, paths);

                RemoveItems<JProperty>(
                    swagger[SwaggerProperties.Definitions],
                    paths,
                    (i) => $"$..[?(@*.$ref == '#/definitions/{i.Name}')]",
                    (i) => $"$..[?(@*.*.items.$ref == '#/definitions/{i.Name}')]");
                if (swagger["tags"] != null)
                {
                    RemoveItems<JObject>(
                        swagger[SwaggerProperties.Tags],
                        paths,
                        (i) => $"$..tags[?(@ == '{i[SwaggerProperties.TagName]}')]");
                }
            }

            return swagger.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private void RemovePaths(IEnumerable<ReRouteOptions> reRoutes, JToken paths)
        {
            var forRemove = new List<JProperty>();

            for (int i = 0; i < paths.Count(); i++)
            {
                var path = paths.ElementAt(i) as JProperty;
                string downstreamPath = path.Name.RemoveSlashFromEnd();
                var reRoute = FindReRoute(reRoutes, downstreamPath);

                if (reRoute != null && RemoveMethods(path, reRoute))
                {
                    RenameToken(path, ReplaceFirst(downstreamPath, reRoute.DownstreamPath, reRoute.UpstreamPath));
                }
                else
                {
                    forRemove.Add(path);
                }
            }

            foreach (var p in forRemove)
            {
                p.Remove();
            }
        }

        private bool RemoveMethods(JProperty path, ReRouteOptions reRoute)
        {
            var forRemove = new List<JProperty>();
            var method = path.First.First as JProperty;

            while (method != null)
            {
                if (!reRoute.ContainsHttpMethod(method.Name))
                {
                    forRemove.Add(method);
                }
                method = method.Next as JProperty;
            }

            foreach (var m in forRemove)
            {
                m.Remove();
            }

            return path.First.Any();
        }

        private static void RemoveItems<T>(JToken token, JToken paths, params Func<T, string>[] searchPaths)
            where T : class
        {
            var forRemove = token
                .Cast<T>()
                .Where(i => searchPaths.Select(p
                    => paths.SelectTokens(p(i)).Any()).All(p => !p))
                .ToList();

            if (typeof(T) == typeof(JProperty))
            {
                CheckSubreferences(token, searchPaths, forRemove);
            }

            foreach (var item in forRemove)
            {
                if (item is JObject o)
                {
                    o.Remove();
                }
                else if (item is JProperty t)
                {
                    t.Remove();
                }
            }
        }

        private static void CheckSubreferences<T>(IEnumerable<JToken> token, Func<T, string>[] searchPaths, List<T> forRemove)
            where T : class
        {
            var notForRemove = token.Cast<T>().Where(t => !forRemove.Contains(t)).Cast<JProperty>().ToList();
            var subReference = forRemove
                    .Cast<JProperty>()
                    .Where(i
                    => searchPaths
                        .Select(p => notForRemove.Any(t => t.SelectTokens(p(i as T)).Any())).Any(p => p))
                    .ToDictionary(p => p.Name, p => p);

            forRemove.RemoveAll(p => subReference.ContainsKey((p as JProperty).Name));

            if (subReference.Count > 0)
            {
                CheckSubreferences(subReference.Values, searchPaths, forRemove);
            }
        }

        private static ReRouteOptions FindReRoute(IEnumerable<ReRouteOptions> reRoutes, string downstreamPath)
            => reRoutes.FirstOrDefault(p
                => p.CanCatchAll
                ? downstreamPath.StartsWith(p.DownstreamPath, StringComparison.CurrentCultureIgnoreCase)
                : p.DownstreamPath.Equals(downstreamPath, StringComparison.CurrentCultureIgnoreCase));

        private static void RemoveHost(JObject swagger)
        {
            swagger.Remove(SwaggerProperties.Host);
            swagger.Remove(SwaggerProperties.Schemes);
        }

        private string ReplaceFirst(string text, string search, string replace)
        {
            int pos = text.IndexOf(search, StringComparison.CurrentCultureIgnoreCase);
            if (pos < 0)
            {
                return text;
            }
            return $"{text.Substring(0, pos)}{replace}{text.Substring(pos + search.Length)}";
        }

        private static void RenameToken(JProperty property, string newName)
        {
            var newProperty = new JProperty(newName, property.Value);
            property.Replace(newProperty);
        }
    }
}