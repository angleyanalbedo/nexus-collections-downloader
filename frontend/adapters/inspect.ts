import { modResource } from './dom';
export default function inspectCards(): string {
  const resources = [...document.querySelectorAll('.collection-mod-row')]
    .map(modResource)
    .filter(Boolean)
    .map((resource) => ({
      modId: resource!.modId,
      name: resource!.name,
      game: resource!.game,
      pictureUrl: resource!.pictureUrl,
      files: resource!.files?.map((file) => ({
        fileId: file.fileId,
        name: file.name,
        version: file.version,
        sizeInBytes: file.sizeInBytes,
        optional: file.optional,
      })),
    }));
  return JSON.stringify({ resources });
}
