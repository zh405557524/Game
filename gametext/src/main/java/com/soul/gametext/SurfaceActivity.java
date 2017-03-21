package com.soul.gametext;

import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.os.SystemClock;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;

import com.soul.library.BaseActivity;
import com.soul.library.utils.LogUtils;
import com.soul.library.utils.ThreadFactory;

public class SurfaceActivity extends BaseActivity implements SurfaceHolder.Callback {


    private SurfaceView mSfv;
    private SurfaceHolder mHolder;
    private boolean flag = false;

    @Override
    protected int getLayoutId() {
        return R.layout.activity_surface;
    }

    @Override
    protected void initView() {
        View bt = findViewById(R.id.bt_uiUpdateUi);
        mSfv = (SurfaceView) findViewById(R.id.sfv);
        bt.setOnClickListener(this);
        mHolder = mSfv.getHolder();
    }

    @Override
    protected void initData() {

    }

    @Override
    protected void initEvent() {
        mHolder.addCallback(this);
    }

    @Override
    protected void otherViewClick(View view) {
        switch (view.getId()) {
            case R.id.bt_uiUpdateUi:
                //开一个线程
                ThreadFactory.getNormaPool().execute(new Runnable() {

                    @Override
                    public void run() {
                        int radius = 0;

                        while (flag) {

                            SystemClock.sleep(5);
                            //更新ui
                            //surfaceView子线更新UI，是有条件
                            //控制器去锁定一个画布 ，或者叫做得到一个加锁的画布
                            Canvas canvas = mHolder.lockCanvas();///锁操作,如果activity销毁，这个时候拿不到一个canvas
                            //因为surface比较消耗资料，在activity在销毁的时候我们surfaceView的资源已经被回收了
                            //所以我们需要监听surfaceView的生命周期.

                            Paint paint = new Paint();
                            paint.setColor(Color.RED);

                            //                            int left = 10;
                            //                            int top = 10;
                            //                            int right = 110;
                            //                            int bottom = 110;
                            //                            RectF rectF = new RectF(left, top, right, bottom);
                            //                            canvas.drawRect(rectF, paint);
                            //                            canvas.drawCircle(圆心的x，圆心的y，半径，画笔);
                                canvas.drawCircle(200, 200, radius++, paint);
                            mHolder.unlockCanvasAndPost(canvas);//解锁操作,就是提交canvas上面的操作

                        }
                    }
                });


                break;
        }
    }

    /**
     * ------------------------------sufaceView 生命周期相关的方法 start--------------------------------
     */
    @Override
    public void surfaceCreated(SurfaceHolder surfaceHolder) {//创建
        LogUtils.i("--------------surfaceCreated------------");
        flag = true;
    }

    @Override
    public void surfaceChanged(SurfaceHolder surfaceHolder, int i, int i1, int i2) {
        //改变
        LogUtils.i("--------------surfaceChanged------------");

    }

    @Override
    public void surfaceDestroyed(SurfaceHolder surfaceHolder) {
        //销毁
        LogUtils.i("--------------surfaceDestroyed------------");
        flag = false;

    }
    /**------------------------------sufaceView 生命周期相关的方法 end--------------------------------*/
}
