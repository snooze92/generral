import { useQueryClient } from '@tanstack/react-query';
import useApiMutation, {
  addOrUpdateQueryClientItem,
} from 'Helpers/Hooks/useApiMutation';
import { useInvalidateTagDetails } from './useTagDetails';
import { Tag } from './useTags';

/** Adds a tag (POST returns the existing tag for a known label) or renames one. */
const useSaveTag = (id: number | undefined, onSaved: (tag: Tag) => void) => {
  const queryClient = useQueryClient();
  const invalidateTagDetails = useInvalidateTagDetails();

  const { mutate, isPending, error } = useApiMutation<Tag, Partial<Tag>>({
    path: id ? `/tag/${id}` : '/tag',
    method: id ? 'PUT' : 'POST',
    mutationOptions: {
      onSuccess: (tag) => {
        queryClient.setQueryData<Tag[]>(['/tag'], (oldData = []) =>
          addOrUpdateQueryClientItem(oldData, tag, 'id')
        );
        invalidateTagDetails();
        onSaved(tag);
      },
    },
  });

  return {
    saveTag: (label: string) => mutate(id ? { id, label } : { label }),
    isSaving: isPending,
    saveError: error,
  };
};

export default useSaveTag;
