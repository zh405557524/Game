package com.soul.game;

import android.graphics.drawable.Drawable;
import android.support.v4.content.ContextCompat;

import com.soul.game.utils.LogUtils;
import com.soul.game.utils.UIUtils;


/**
 * * @author soul
 *
 * @项目名:MyApplication
 * @包名: com.soul.game
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/25 19:38
 */
public class PictureChooseUtils {

    private static Drawable drawable_2 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f2);
    private static Drawable drawable_4 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f4);
    private static Drawable drawable_8 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f8);
    private static Drawable drawable_16 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f16);
    private static Drawable drawable_32 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f32);
    private static Drawable drawable_64 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f64);
    private static Drawable drawable_128 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f128);
    private static Drawable drawable_256 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f256);
    private static Drawable drawable_512 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f512);
    private static Drawable drawable_1024 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f1024);
    private static Drawable drawable_2048 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f2048);
//    private static Drawable drawable_4096 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f4096);
//    private static Drawable drawable_8192 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f8192);
//    private static Drawable drawable_16384 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f16384);
//    private static Drawable drawable_32768 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f32768);
//    private static Drawable drawable_65536 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f65536);
//    private static Drawable drawable_131072 = ContextCompat.getDrawable(UIUtils.getContext(), R.drawable.f131072);

    public static Drawable getDrawable(int num) {
        switch (num) {
            case 2:
                return drawable_2;
            case 4:
                return drawable_4;
            case 8:
                return drawable_8;
            case 16:
                return drawable_16;
            case 32:
                return drawable_32;
            case 64:
                return drawable_64;
            case 128:
                return drawable_128;
            case 256:
                return drawable_256;
            case 512:
                return drawable_512;
            case 1024:
                return drawable_1024;
            case 2048:
                return drawable_2048;
//            case 4096:
//                return drawable_4096;
//            case 8192:
//                return drawable_8192;
//            case 16384:
//                return drawable_16384;
//            case 32768:
//                return drawable_32768;
//            case 65536:
//                return drawable_65536;
//            case 131072:
//                return drawable_131072;
        }
        return null;
    }


    private static int sAnimalDuration = 250;
    private static int sAnimalDurationMax = 600;

    public static void setAnimalDuration(int animalDuration) {
        sAnimalDuration = animalDuration;
    }

    public static int getsAnimalDuration() {
        return sAnimalDuration;
    }

    public static void setsAnimalDuration(int duration){
        int i = sAnimalDurationMax-(int) (((float) duration / 100.f) * (float)sAnimalDurationMax );
        LogUtils.i("speed:"+i);
        sAnimalDuration = i;;
    }

}
