package com.soul.gametext;

import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Point;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.gametext
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/31 14:24
 */

public class Sprite {

    public Bitmap mBitmap;//任何一个元素其实就对应一个图片
    public Point mPoint;//任何一个元素其实就对应一个位置

    public Sprite(Bitmap bitmap, Point point) {
        super();

        if (point == null) {
            mPoint = new Point(0, 0);//如果初始化的时候没有传递point，那就默认放到左上角
        } else {
            this.mPoint = point;
        }
        mBitmap = bitmap;
    }

    /**
     * 共有的一个方法就是绘制自身
     *
     * @param canvas
     */
    public void drawSelf(Canvas canvas) {
        //canvas.drawBitmap(图片，图片距离左边的距离，图片距离右边的一个距离，画笔);
        canvas.drawBitmap(mBitmap, mPoint.x, mPoint.y, null);
    }


}
