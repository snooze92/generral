import { Provider } from 'Settings/Provider/Provider';

export type DownloadProtocol = 'torrent' | 'usenet' | 'unknown';

interface Indexer extends Provider {
  enableRss: boolean;
  enableAutomaticSearch: boolean;
  enableInteractiveSearch: boolean;
  supportsRss: boolean;
  supportsSearch: boolean;
  protocol: DownloadProtocol;
  priority: number;
  downloadClientId: number;
}

export interface IndexerOptions {
  /** Minutes, 0 disables RSS sync. */
  rssSyncInterval: number;
}

export const PATH = '/indexer';
export const OPTIONS_PATH = '/settings/indexer';

export default Indexer;
