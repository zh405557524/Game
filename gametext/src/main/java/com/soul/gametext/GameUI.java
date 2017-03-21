package com.soul.gametext;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Point;
import android.graphics.Rect;
import android.os.SystemClock;
import android.util.AttributeSet;
import android.view.MotionEvent;
import android.view.SurfaceHolder;
import android.view.SurfaceView;

import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.gametext
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/31 13:53
 */

public class GameUI extends SurfaceView implements SurfaceHolder.Callback {

    private SurfaceHolder mHolder;
    GameTask gameTask;
    Context context;
    private Man mMan;
    private Face mFace;
    private Face mFace1;
    private Point mamInitPoint = new Point(200, 100);
    private Bottom mBottom;


    /**
     * 1 要得到surfaceViewHolder
     * 2 监听SurfaceVieHolder.Callback
     * 3 需要子线程更新UI
     */

    public GameUI(Context context) {
        this(context, null);
    }

    public GameUI(Context context, AttributeSet attrs) {
        this(context, attrs, 0);
    }

    public GameUI(Context context, AttributeSet attrs, int defStyleAttr) {
        super(context, attrs, defStyleAttr);
        mHolder = this.getHolder();
        mHolder.addCallback(this);
        gameTask = new GameTask();//实例化一个线程
        this.context = context;

    }

    /**
     * 处理cactvity传递过来的事件
     *
     * @param event
     */
    public void handleTouchEvent(MotionEvent event) {
        switch (event.getAction()) {
            case MotionEvent.ACTION_DOWN:
                //按下屏幕，吐出一个笑脸
                int x = (int) event.getRawX();
                int y = (int) event.getRawY();
                Point clickPoint = new Point(x, y);
                //判断是否点击了箭头。

                //判断是否点击了箭头
                boolean isPressed = mBottom.isPressed(clickPoint);
                if (isPressed){
                    //点击范围在江头的范围内
                    //小人
                    mMan.moveBottom();
                }else {
                    //点击箭头以外的区域

                    mFace = mMan.createFace(clickPoint);
                    faces.add(mFace);
                }


                break;
            case MotionEvent.ACTION_MOVE:
                break;
            case MotionEvent.ACTION_UP:
                mBottom.setPressed(false);
                break;
        }

    }


    boolean flag;

    class GameTask extends Thread {
        @Override
        public void run() {

            while (flag) {
                //  这个里面就是需要去更新ui操作
                SystemClock.sleep(1);
                drawUI();
            }

        }

    }

    ;

    private List<Face> faces = new CopyOnWriteArrayList<>();

    /**
     * 所有的绘制操作就放到这个方法里面来
     */
    private void drawUI() {
        //得到一个加锁的画布--->锁一个画布
        Canvas canvas = mHolder.lockCanvas();
        Paint paint = new Paint();
        paint.setColor(Color.RED);
        canvas.drawRect(10, 10, getWidth(), getHeight(), paint);
        mMan.drawSelf(canvas);
        //遍历集合绘制笑脸


        for (Face face : faces) {
            Rect rectScreen = new Rect(0, 0, getWidth(), getHeight());
            //如何face所在的位置不在屏幕
            if (!rectScreen.contains(face.mPoint.x,face.mPoint.y)){
                faces.remove(face);
            }
            face.drawSelf(canvas);
            face.move();
        }

        //绘制bottom
        mBottom.drawSelf(canvas);


        if (mFace != null) {
            mFace.drawSelf(canvas);
            mFace.move();
        }
        //释放锁-->解锁一个画布
        mHolder.unlockCanvasAndPost(canvas);

    }


    /**
     * ------------------------------监听 surface生命周期start--------------------------------
     */

    @Override
    public void surfaceCreated(SurfaceHolder surfaceHolder) {
        flag = true;//线程可以执行
        mMan = new Man(mamInitPoint, context);
        //创建bottom
        Point bottomInitPoint = new Point(200,getHeight()-200);
        mBottom = new Bottom(bottomInitPoint,context);
        gameTask.start();//启动绘制ui的线程



    }

    @Override
    public void surfaceChanged(SurfaceHolder surfaceHolder, int i, int i1, int i2) {

    }

    @Override
    public void surfaceDestroyed(SurfaceHolder surfaceHolder) {
        flag = false;//线程不可以执行

    }

    /**------------------------------监听 surface生命周期end--------------------------------*/


}
