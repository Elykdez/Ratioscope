package com.sfb.standalonefilebrowser;

import android.app.Activity;
import android.app.Fragment;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

public class StandaloneFileBrowserFragment extends Fragment {
    public static final int PICKFILE_RESULT_CODE = 1;
	public static final int SAVEFILE_RESULT_CODE = 2;
	
    private StandaloneFileBrowserAndroidListener fileBrowserAndroidListener;
    private StandaloneFileBrowserAndroidSaveListener saveFileBrowserAndroidListener;
    private String title;
    private Boolean multiple;
    private String defaultName;
    private byte[] saveData;
    private boolean pickerStarted;

    public void setFileBrowserAndroidListener(final StandaloneFileBrowserAndroidListener fileBrowserAndroidListener) {
        this.fileBrowserAndroidListener = fileBrowserAndroidListener;
    }

    public void setSaveFileBrowserAndroidListener(final StandaloneFileBrowserAndroidSaveListener saveFileBrowserAndroidListener) {
        this.saveFileBrowserAndroidListener = saveFileBrowserAndroidListener;
    }

    public void setSaveData(final String defaultName, final byte[] saveData) {
        this.defaultName = defaultName;
        this.saveData = saveData;
    }
	
	public void setTitle(final String title) {
		this.title = title;
	}
	
	public void setMultiple(final Boolean multiple) {
		this.multiple = multiple;
	}

    @Override
    public void onStart () {
        super.onStart ();

        // onStart runs again when the document picker returns control to Unity.
        // Starting another intent on that second call makes both open and save
        // dialogs appear twice for a single button press.
        if (pickerStarted) {
            return;
        }
        pickerStarted = true;

        if (saveFileBrowserAndroidListener != null) {
            final Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("text/plain");
            intent.putExtra(Intent.EXTRA_TITLE, defaultName);
            if (title != null) {
                intent.putExtra("android.intent.extra.TITLE", title);
            }
            startActivityForResult(intent, SAVEFILE_RESULT_CODE);
            return;
        }

        final Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
        if (title != null) {
            intent.putExtra("android.intent.extra.TITLE", title);
        }
        if (multiple) {
            intent.putExtra("android.intent.extra.ALLOW_MULTIPLE", true);
        }
        intent.setType("*/*");
        startActivityForResult(intent, PICKFILE_RESULT_CODE);
    }

    private String getFileCopyPath(final Uri uri) {
        if (uri == null) {
            return null;
        }
        final ContentResolver contentResolver = getActivity().getContentResolver();
        Cursor cursor = null;
        String filename = null;
        try {
            cursor = contentResolver.query(uri, null, null, null, null);
            if (cursor != null && cursor.moveToFirst()) {
                filename = cursor.getString(cursor.getColumnIndex("_display_name"));
            }
        }
        catch (Exception e) {
            return null;
        }
        finally {
            if (cursor != null) {
                cursor.close();
            }
        }
        try {
            final InputStream input = contentResolver.openInputStream(uri);
            if (input == null) {
                return null;
            }
            final File file = new File(getActivity().getCacheDir(), filename);
            OutputStream output = null;
            try {
                output = new FileOutputStream(file, false);
                final byte[] buffer = new byte[4096];
                int len;
                while ((len = input.read(buffer)) > 0) {
                    output.write(buffer, 0, len);
                }
                return file.getAbsolutePath();
            }
            finally {
                if (output != null) {
                    output.close();
                }
                input.close();
            }
        }
        catch (Exception e) {
            return null;
        }
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        switch (requestCode) {
            case PICKFILE_RESULT_CODE:
                String filenames = "";
                if (resultCode == Activity.RESULT_OK && data != null) {
                    if (data.getClipData() != null) {
                        for (int i = 0; i <  data.getClipData().getItemCount(); ++i) {
                            if (!filenames.isEmpty()) {
                                filenames += "|";
                            }
                            filenames += getFileCopyPath(data.getClipData().getItemAt(i).getUri());
                        }
                    }
                    else if (data.getData() != null) {
                        filenames = getFileCopyPath(data.getData());
                    }
                }
                if (fileBrowserAndroidListener != null) {
                    fileBrowserAndroidListener.onFilesSelected(filenames);
                }
                break;
            case SAVEFILE_RESULT_CODE:
                boolean saved = false;
                if (resultCode == Activity.RESULT_OK && data != null && data.getData() != null) {
                    try {
                        final OutputStream output = getActivity().getContentResolver().openOutputStream(data.getData(), "wt");
                        if (output != null) {
                            try {
                                output.write(saveData == null ? new byte[0] : saveData);
                                saved = true;
                            }
                            finally {
                                output.close();
                            }
                        }
                    }
                    catch (Exception ignored) {
                        saved = false;
                    }
                }
                if (saveFileBrowserAndroidListener != null) {
                    saveFileBrowserAndroidListener.onFileSaved(saved);
                }
                break;
        }
        getFragmentManager().beginTransaction().remove(this).commit();
    }
}

