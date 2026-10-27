// The one place that knows the project's name and addresses.
// Everything on the site that mentions the library reads it from here:
// astro.config.mjs, scripts/prepare.mjs, the home pages and the layout.
// When the library is renamed, change this file (and the texts in docs/, README and CHANGELOG, which are
// the source of truth and are not rewritten by the site).

export const project = {
  /** Display name of the library. */
  name: 'IronMapper',
  /** One line under the name on the home page and in metadata. */
  tagline: {
    en: 'A compile-time object mapper for .NET: a Roslyn source generator writes the mapping code as plain C# when you build.',
    ru: 'Маппер объектов для .NET времени компиляции: Roslyn source generator пишет код маппинга как обычный C# при сборке.',
  },
  /** Author and copyright holder. */
  author: 'Aleksey Mironov',
  /** GitHub organization and repository. */
  github: {
    owner: 'iron-mapper',
    repo: 'IronMapper',
    /** Branch that the site links to for files in the repository. */
    branch: 'main',
  },
  /** NuGet packages, the main one first. */
  nuget: ['IronMapper', 'IronMapper.Extensions.DI'],
  /** Where the site is served from. GitHub Pages of the organization: https://<owner>.github.io/<repo>/ */
  site: 'https://iron-mapper.github.io',
  base: '/IronMapper',
};

export const githubUrl = `https://github.com/${project.github.owner}/${project.github.repo}`;
export const nugetUrl = (pkg) => `https://www.nuget.org/packages/${pkg}`;
/** Link to a file of the repository on GitHub. `path` is relative to the repository root. */
export const blobUrl = (path) => `${githubUrl}/blob/${project.github.branch}/${path.replace(/^\/+/, '')}`;
