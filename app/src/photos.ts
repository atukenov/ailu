import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';
import * as ImagePicker from 'expo-image-picker';
import { Platform } from 'react-native';

// Roadmap: shrink on the phone before upload. 2 560 px on the long side at JPEG 82 takes a 3.5 MB
// photo to about 1.2 MB with no visible difference on a phone, so the couple's quota lasts 3× longer.
const LONG_SIDE = 2560;
const QUALITY = 0.82;

export const MAX_PHOTOS = 10;

export type Picked = { uri: string; width: number; height: number };

export async function pickPhotos(limit: number): Promise<Picked[]> {
  const result = await ImagePicker.launchImageLibraryAsync({
    mediaTypes: ['images'],
    allowsMultipleSelection: limit > 1,
    selectionLimit: limit,
    quality: 1,
    exif: false,
  });
  if (result.canceled) return [];
  return result.assets.slice(0, limit).map((a) => ({ uri: a.uri, width: a.width, height: a.height }));
}

export async function toUpload(photo: Picked): Promise<FormData> {
  const context = ImageManipulator.manipulate(photo.uri);
  if (Math.max(photo.width, photo.height) > LONG_SIDE)
    context.resize(photo.width >= photo.height ? { width: LONG_SIDE } : { height: LONG_SIDE });
  const image = await context.renderAsync();
  const saved = await image.saveAsync({ compress: QUALITY, format: SaveFormat.JPEG });

  const form = new FormData();
  if (Platform.OS === 'web') {
    form.append('file', await (await fetch(saved.uri)).blob(), 'photo.jpg');
  } else {
    // React Native's FormData takes a file reference in this shape.
    form.append('file', { uri: saved.uri, name: 'photo.jpg', type: 'image/jpeg' } as unknown as Blob);
  }
  return form;
}
