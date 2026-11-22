import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export const NAMING_PATH = '/config/naming';
export const MEDIA_MANAGEMENT_PATH = '/config/mediamanagement';
export const ROOT_FOLDER_PATH = '/rootfolder';
export const REMOTE_PATH_MAPPING_PATH = '/remotepathmapping';

export interface NamingConfig {
  renameEpisodes: boolean;
  replaceIllegalCharacters: boolean;
  colonReplacementFormat: number;
  customColonReplacementFormat: string;
  multiEpisodeStyle: number;
  standardEpisodeFormat: string;
  dailyEpisodeFormat: string;
  seriesFolderFormat: string;
  seasonFolderFormat: string;
  specialsFolderFormat: string;
}

export interface NamingExamples {
  singleEpisodeExample?: string;
  multiEpisodeExample?: string;
  dailyEpisodeExample?: string;
  seriesFolderExample?: string;
  seasonFolderExample?: string;
  specialsFolderExample?: string;
}

export interface MediaManagementConfig {
  copyUsingHardlinks: boolean;
  recycleBin: string;
  recycleBinCleanupDays: number;
  skipFreeSpaceCheckWhenImporting: boolean;
  minimumFreeSpaceWhenImporting: number;
  importExtraFiles: boolean;
  extraFileExtensions: string;
  deleteEmptyFolders: boolean;
  setPermissionsLinux: boolean;
  chmodFolder: string;
  chownGroup: string;
}

export interface RootFolder extends ModelBase {
  path: string;
  accessible: boolean;
  freeSpace?: number;
  totalSpace?: number;
  unmappedFolders: { name: string; path: string; relativePath: string }[];
}

export interface RemotePathMapping extends ModelBase {
  host: string;
  remotePath: string;
  localPath: string;
}

const NO_ROOT_FOLDERS: RootFolder[] = [];

export const useRootFolders = () => {
  const result = useApiQuery<RootFolder[]>({ path: ROOT_FOLDER_PATH });

  return { ...result, data: (result.data ?? NO_ROOT_FOLDERS) as RootFolder[] };
};
