import { defineCollection } from 'astro:content';
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';

// src/content/docs/ is written by scripts/prepare.mjs before every dev/build run; see site/README.md.
// Page ids are the file paths as they are (`upgrading-to-2.1`, `ru/configuration`): the default id generation
// would slugify them (`upgrading-to-21`), and the site keeps the file names of docs/ as its addresses.
export const collections = {
  docs: defineCollection({
    loader: docsLoader({
      generateId: ({ entry }) => entry.replace(/\.mdx?$/, '').replace(/\/index$/, ''),
    }),
    schema: docsSchema(),
  }),
};
