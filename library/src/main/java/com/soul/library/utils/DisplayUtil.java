package com.soul.library.utils;

import android.app.Activity;
import android.content.Context;
import android.content.res.Configuration;
import android.graphics.Point;
import android.graphics.SurfaceTexture;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.os.Build;
import android.util.DisplayMetrics;
import android.util.Size;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.ListAdapter;
import android.widget.ListView;

import java.util.ArrayList;
import java.util.List;

/**
 * author: jk
 * created on: 2016/8/11 17:14
 * description: dp、sp 转换为 px 的工具类
 */
public class DisplayUtil {
    private static DisplayUtil   displayUtil = null;
    public DisplayUtil(){}
    public static DisplayUtil getInstance(){
        if (displayUtil!=null)
        return displayUtil;
        return new DisplayUtil();
    }
    /**
     * 将px值转换为dip或dp值，保证尺寸大小不变
     *
     * @param pxValue
     *            （DisplayMetrics类中属性density）
     * @return
     */
    public static int px2dip(Context context, float pxValue) {
        final float scale = context.getResources().getDisplayMetrics().density;
        return (int) (pxValue / scale + 0.5f);
    }

    /**
     * 将dip或dp值转换为px值，保证尺寸大小不变
     *
     * @param dipValue

     * @return
     */
    public static int dip2px(Context context, float dipValue) {
        final float scale = context.getResources().getDisplayMetrics().density;
        return (int) (dipValue * scale + 0.5f);
    }

    /**
     * 隐藏虚拟按键
     * @param context
     */
    public static void hideNativeButton(Context context){
        if (Build.VERSION.SDK_INT > 11 && Build.VERSION.SDK_INT < 19) { // lower api
            View v = ((Activity)context).getWindow().getDecorView();
            v.setSystemUiVisibility(View.GONE);
        } else if (Build.VERSION.SDK_INT >= 19) {
            //for new api versions.
            View decorView = ((Activity)context).getWindow().getDecorView();
            int uiOptions = View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_FULLSCREEN;
            decorView.setSystemUiVisibility(uiOptions);
        }
    }

    /**
     * 将px值转换为sp值，保证文字大小不变
     *
     * @param pxValue
     *            （DisplayMetrics类中属性scaledDensity）
     * @return
     */
    public static int px2sp(Context context, float pxValue) {
        final float fontScale = context.getResources().getDisplayMetrics().scaledDensity;
        return (int) (pxValue / fontScale + 0.5f);
    }

    /**
     * 将sp值转换为px值，保证文字大小不变
     *
     * @param spValue
     *            （DisplayMetrics类中属性scaledDensity）
     * @return
     */
    public static int sp2px(Context context, float spValue) {
        final float fontScale = context.getResources().getDisplayMetrics().scaledDensity;
        return (int) (spValue * fontScale + 0.5f);
    }
    /**
     * 获取屏幕宽度和高度，单位为px
     * @param context
     * @return
     */
    public static Point getScreenMetrics(Context context){
        DisplayMetrics dm =context.getResources().getDisplayMetrics();
        int w_screen = dm.widthPixels;
        int h_screen = dm.heightPixels;
        return new Point(w_screen, h_screen);

    }

    /**
     * 获取屏幕长宽比
     * @param context
     * @return
     */
    public  static float getScreenRate(Context context){
        Point P = getScreenMetrics(context);
        float H = P.y;
        float W = P.x;
        return (H/W);
    }

    /**
     * 获取当前系统的
     * @param context
     * @return
     */
    public static Point getWindowPoint(Context context){
        WindowManager wm1 = ((Activity)context).getWindowManager();

        Point size = new Point();
        wm1.getDefaultDisplay().getRealSize(size);
//        wm1.getDefaultDisplay().getSize(size);

        return size;
    }


    /**
     * 通过对比得到与宽高比最接近的尺寸（如果有相同尺寸，优先选择）
     *
     * @param surfaceWidth
     *            需要被进行对比的原宽
     * @param surfaceHeight
     *            需要被进行对比的原高
     * @param preSizeList
     *            需要对比的预览尺寸列表
     * @return 得到与原宽高比例最接近的尺寸
     */
    public static Size getCloselyPreSize(Context context, int surfaceWidth, int surfaceHeight,
                                         Size[] preSizeList) {

        int ReqTmpWidth;
        int ReqTmpHeight;
        boolean mIsPortrait = false;
        // 当屏幕为垂直的时候需要把宽高值进行调换，保证宽大于高
        int orientation = context.getResources().getConfiguration().orientation;
        if(orientation == Configuration.ORIENTATION_PORTRAIT){
            mIsPortrait = true;
        }else {
            mIsPortrait = false;
        }
        if (mIsPortrait) {
            ReqTmpWidth = surfaceHeight;
            ReqTmpHeight = surfaceWidth;
        } else {
            ReqTmpWidth = surfaceWidth;
            ReqTmpHeight = surfaceHeight;
        }
        //先查找preview中是否存在与surfaceview相同宽高的尺寸
        for(Size size : preSizeList){
            if((size.getWidth() == ReqTmpWidth) && (size.getHeight() == ReqTmpHeight)){
                return size;
            }
        }

        // 得到与传入的宽高比最接近的size
        float reqRatio = ((float) ReqTmpWidth) / ReqTmpHeight;
        float curRatio, deltaRatio;
        float deltaRatioMin = Float.MAX_VALUE;
        Size retSize = null;
        for (Size size : preSizeList) {
            if (size.getWidth()<2000){
                curRatio = ((float) size.getHeight()) / size.getHeight();
                deltaRatio = Math.abs(reqRatio - curRatio);
                if (deltaRatio < deltaRatioMin) {
                    deltaRatioMin = deltaRatio;
                    retSize = size;
                }
            }

        }

        return retSize;
    }

    public static void setListViewHeightBasedOnChildren(ListView listView) {
        ListAdapter listAdapter = listView.getAdapter();
        if (listAdapter == null) {
            // pre-condition
            return;
        }

        int totalHeight = 0;
        for (int i = 0; i < listAdapter.getCount(); i++) {
            View listItem = listAdapter.getView(i, null, listView);
            listItem.measure(0, 0);
            totalHeight += listItem.getMeasuredHeight();
        }

        ViewGroup.LayoutParams params = listView.getLayoutParams();
        params.height = totalHeight + (listView.getDividerHeight() * (listAdapter.getCount() - 1));
        listView.setLayoutParams(params);
    }


    /**
     * 选择出合适的尺寸
     * @param outputSizes
     * @param textureWidth
     * @param textureHight
     * @return
     */
    public static Size chooseSuitAble(Size[] outputSizes, int textureWidth, int textureHight){
        ArrayList<Size> checkOutSize = new ArrayList<>();
        //选择出和手机尺寸一致的尺寸
        for (Size output : outputSizes){
            if (((float)output.getWidth())/output.getHeight() == ((float)textureWidth)/textureHight){
                checkOutSize.add(output);
            }
        }
        //如果有,挑选出一个宽度小于2000，且最接近2000的
        Size tempSize = new Size(0,0);

        if (checkOutSize.size()>0){
            for(Size checkOut : checkOutSize){
                if (checkOut.getHeight() == textureHight){
                    return checkOut;
                }

                if (checkOut.getHeight()<2000){

                    //选出最大的
                    if (checkOut.getHeight() - tempSize.getHeight() >-1){
                        tempSize = checkOut;
                    }

                }

            }

            return tempSize;
        }
        return outputSizes[1];
    }

    /**
     *  挑选出合适的分辨率
     * @param context
     * @param sizes
     * @return
     */
    public static List<Size> filterResolution(Context context, Size[] sizes){
        Point screenPoint = DisplayUtil.getWindowPoint(context);
        int windowX = screenPoint.x;
        int windowY = screenPoint.y;
        ArrayList<Size> filters = new ArrayList<Size>();
        for (Size size : sizes){
//            if (((float)size.getHeight())/((float)size.getWidth()) == ((float)windowY)/((float)windowX)){
//                filters.add(size);
//            }
            if (size.getHeight()*windowY == size.getWidth()*windowX){
                filters.add(size);
            }


        }

        return filters;

    }

    /**
     * 获取所有支持的视频输出尺寸
     * @param characteristics
     * @return
     */
    public static Size[] getSupportPreviewSizes(CameraCharacteristics characteristics){
        StreamConfigurationMap map = characteristics
                .get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
        Size[] outputSizes = map.getOutputSizes(SurfaceTexture.class);

        return  outputSizes;
    }


}
