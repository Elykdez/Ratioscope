package com.sfb.standalonefilebrowser;

import android.app.Activity;
public class StandaloneFileBrowser {
    public static void showOpenFileDialog(final Activity activity, final String title, final boolean selectMultiple, final StandaloneFileBrowserAndroidListener listener) {
        StandaloneFileBrowserFragment fragment = new StandaloneFileBrowserFragment();
        fragment.setFileBrowserAndroidListener(listener);
        fragment.setMultiple(selectMultiple);
        fragment.setTitle(title);
        activity.getFragmentManager().beginTransaction().add(0, fragment).commit();
    }

    public static void showSaveFileDialog(final Activity activity, final String title, final String defaultName, final byte[] data, final StandaloneFileBrowserAndroidSaveListener listener) {
        StandaloneFileBrowserFragment fragment = new StandaloneFileBrowserFragment();
        fragment.setSaveFileBrowserAndroidListener(listener);
        fragment.setSaveData(defaultName, data);
        fragment.setTitle(title);
        activity.getFragmentManager().beginTransaction().add(0, fragment).commit();
    }
}
