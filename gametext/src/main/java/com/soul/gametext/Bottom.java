package com.soul.gametext;

import android.content.Context;
import android.content.res.Resources;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Point;
import android.graphics.Rect;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.gametext
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/3/6 17:02
 */

public class Bottom extends Sprite {

    private Bitmap pressedBitmap;
    private boolean isPressed;
     public Bottom( Point point,Context context) {
        super(null, point);
         Resources res = context.getResources();
         this.mBitmap  = BitmapFactory.decodeResource(res, R.drawable.bottom_default);
         this.pressedBitmap  = BitmapFactory.decodeResource(res,R.drawable.bottom_press);
    }
    //默认情况，只会回执bimap
    @Override
    public void drawSelf(Canvas canvas) {
        if (isPressed){//如果是按下去的时候，就应该绘制按下去的那个图片
        canvas.drawBitmap(pressedBitmap,this.mPoint.x,this.mPoint.y,null);
        }else {
            super.drawSelf(canvas);
        }


    }

    /**
     * 用户点击的位置
     * @param clickPoint
     * @return
     */
    public boolean isPressed(Point clickPoint) {//返回isPressed的true/false的结果
        Rect bottomRect = new Rect(this.mPoint.x,this.mPoint.y,this.mPoint.x
        +this.mBitmap.getWidth(),this.mPoint.y+this.mBitmap.getHeight());


        //1 用户点击范围在箭头所在范围
        if (bottomRect.contains(clickPoint.x,clickPoint.y)){
            isPressed = true;
        }else {
            //2 用户点击范围不在箭头所在范围
            isPressed = false;
        }

        return isPressed;
    }

    public void setPressed(boolean pressed) {

        isPressed = pressed;
    }
}
