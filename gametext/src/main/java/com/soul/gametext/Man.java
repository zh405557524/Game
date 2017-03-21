package com.soul.gametext;

import android.content.Context;
import android.graphics.BitmapFactory;
import android.graphics.Point;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.gametext
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/31 14:45
 */

public class Man extends Sprite {

    Context mContext;

    public Man(Point point, Context context) {
        super(null, point);
        mContext = context;
        this.mBitmap = BitmapFactory.decodeResource(context.getResources(),R.drawable.avatar_boy);

    }

    /**
     * 吐笑脸/创建笑脸
     */

    public Face createFace(Point clickPoint) {

        Face face = new Face(new Point(this.mPoint.x + 60, this.mPoint.y + 50),clickPoint,mContext );
        return face;
    }

    /**
     * 小人往下走
     */
    public void moveBottom() {
        this.mPoint.y+=10;

    }
}
