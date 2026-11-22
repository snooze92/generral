import { DownloadProtocol } from 'Settings/Indexers/Indexer';
import { Provider } from 'Settings/Provider/Provider';

interface DownloadClient extends Provider {
  enable: boolean;
  protocol: DownloadProtocol;
  priority: number;
  removeCompletedDownloads: boolean;
  removeFailedDownloads: boolean;
}

export interface DownloadClientOptions {
  autoRedownloadFailed: boolean;
  autoRedownloadFailedFromInteractiveSearch: boolean;
  /** Hours, 0 disables stalled handling. */
  stalledDownloadTimeout: number;
}

export const PATH = '/downloadclient';
export const OPTIONS_PATH = '/settings/downloadclient';

export default DownloadClient;
