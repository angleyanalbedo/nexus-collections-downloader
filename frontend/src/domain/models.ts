export type PageKind = 'games' | 'mods' | 'collections' | 'collection' | 'detail' | 'files';
export interface SiteItem {
  id: string;
  name: string;
  url: string;
  kind: 'game' | 'mod' | 'collection' | 'file';
  image?: string;
  meta?: string;
  group?: string;
  fileId?: string;
  downloadUrl?: string;
  modName?: string;
  version?: string;
  size?: number;
  optional?: boolean | null;
  collectionName?: string;
  revision?: string;
}
export interface CollectionManifest {
  key: string;
  revision: string;
  expectedFiles: number;
  complete: boolean;
  message: string;
  files: SiteItem[];
}
export interface PageSnapshot {
  title: string;
  url: string;
  kind: PageKind;
  status: 'ready' | 'loading' | 'challenge' | 'login' | 'error';
  description: string;
  resultText: string;
  items: SiteItem[];
  navigation: { name: string; url: string }[];
  collection?: CollectionManifest | null;
}
export type TaskStatus =
  | 'queued'
  | 'waiting'
  | 'downloading'
  | 'completed'
  | 'paused'
  | 'failed'
  | 'cancelled';
export interface DownloadTask {
  id: string;
  file: SiteItem;
  status: TaskStatus;
  error: string;
  path: string;
  received: number;
  total: number;
  canResume: boolean;
}
export interface HostState {
  type: 'state';
  tasks: DownloadTask[];
  snapshot: PageSnapshot | null;
  busy: boolean;
  queueEnabled: boolean;
  output: string;
  message: string;
  siteUrl: string;
  browserVisible: boolean;
  initialUrl?: string;
  navigationManagedByHost?: boolean;
}
export type HostCommand =
  | {
      action: 'ready' | 'login' | 'browser' | 'read' | 'start' | 'stop' | 'folder' | 'openFolder';
    }
  | { action: 'pause' | 'resume' | 'cancel' | 'deleteTask'; id: string }
  | { action: 'cancelAll' | 'clearFinished' }
  | { action: 'browse'; url: string }
  | { action: 'search'; scope: 'games' | 'mods' | 'collections'; game: string; query: string }
  | { action: 'enqueue'; ids: string[]; start?: boolean }
  | { action: 'enqueueCollection'; key: string; ids: string[]; start: boolean }
  | { action: 'retry'; id: string };
