package com.soul.cocos2ddemo;

import android.view.MotionEvent;

import org.cocos2d.actions.base.CCRepeatForever;
import org.cocos2d.actions.instant.CCCallFunc;
import org.cocos2d.actions.interval.CCAnimate;
import org.cocos2d.actions.interval.CCMoveTo;
import org.cocos2d.actions.interval.CCSequence;
import org.cocos2d.layers.CCLayer;
import org.cocos2d.layers.CCTMXObjectGroup;
import org.cocos2d.layers.CCTMXTiledMap;
import org.cocos2d.nodes.CCAnimation;
import org.cocos2d.nodes.CCSprite;
import org.cocos2d.nodes.CCSpriteFrame;
import org.cocos2d.nodes.CCTextureCache;
import org.cocos2d.opengl.CCTexture2D;
import org.cocos2d.particlesystem.CCParticleSnow;
import org.cocos2d.particlesystem.CCParticleSystem;
import org.cocos2d.types.CGPoint;
import org.cocos2d.types.CGSize;
import org.cocos2d.types.util.CGPointUtil;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.cocos2ddemo
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/3/16 23:05
 */

public class DemoLayer extends CCLayer {
    /**
     * 移动点的位置
     */
    int index = 0;
    /**
     * 僵尸移动的速度
     */
    private float speed = 50;
    /**
     * 地图
     */
    private CCTMXTiledMap mMap;
    /**
     * 移动点的集合
     */
    private List<CGPoint> mPoints;
    /**
     * 僵尸
     */
    private CCSprite mZombie;
    private CCParticleSystem mSystem;


    public DemoLayer() {
        loadMap();

        loadRoadPoint();
        loadZombie();
        animate();
        move();
        //让ccTouche可以接受到事件
        this.setIsTouchEnabled(true);
        particleSystem();//离子系统
    }

    /**
     * 粒子系统
     */
    private void particleSystem() {

        mSystem = CCParticleSnow.node();
        CCTexture2D tex = CCTextureCache.sharedTextureCache().addImage("snow.png");
        //粒子系统添加一个纹理
        mSystem.setTexture(tex);
        this.addChild(mSystem);

    }


    @Override
    public boolean ccTouchesMoved(MotionEvent event) {
        //让地图可以移动
        mMap.touchMove(event, mMap);
        return super.ccTouchesMoved(event);
    }


    /**
     * 让僵尸走起来
     */
    public void move() {
        index++;
        //t :持续时间,pos:参考点
        //因为，僵尸的move过程是匀速的，所有以不同的路程，走的时间应该是不同的，所以穾动态的去计算
        CGPoint startPoint = mPoints.get(index - 1);
        CGPoint endPoint = mPoints.get(index);

        //index startPoint     endPoint
        // 1    mPoints.get(0) mPoints.get(1)
        if (index < mPoints.size()) {//index -->6

            float distance = CGPointUtil.distance(startPoint, endPoint);
            float t = distance / speed;
            CCMoveTo ccMoveTo = CCMoveTo.action(t, endPoint);
            //走完了之后，继续去走
            //僵尸走完了之后可以递归去掉move方法
            CCSequence ccSequence = CCSequence.actions(ccMoveTo, CCCallFunc.action(this, "move"));
            mZombie.runAction(ccSequence);
        } else {
            //这里代表，我们的僵尸已经走完了所有的路程
            //雪停了
            mSystem.stopSystem();
            //僵尸跳起了骑马舞
            //播放江南style音乐
        }


    }

    /**
     * 让僵尸动起来
     */
    private void animate() {
        //name ccAnimation的别名， delay:动画执行时间 frames:动画所有的帧
        ArrayList<CCSpriteFrame> frames = new ArrayList<>();
        String format = "walk/z_1_%02d.png";//%02d
        //循环得到图片的地址
        for (int i = 1; i <= 7; i++) {

            String path = String.format(format, i);
            CCSpriteFrame frame = CCSprite.sprite(path).displayedFrame();
            frames.add(frame);
        }

        CCAnimation ccAnimation = CCAnimation.animation("", .2f, frames);
        CCAnimate action = CCAnimate.action(ccAnimation);//把animation转化成ccanimate
        //让帧动画一直执行
        CCRepeatForever repeatForever = CCRepeatForever.action(action);
        mZombie.runAction(repeatForever);

    }

    /**
     * 加载僵尸
     */
    private void loadZombie() {
        mZombie = CCSprite.sprite("walk/z_1_01.png");
        mMap.addChild(mZombie);
        //设置僵尸的位置
        mZombie.setPosition(mPoints.get(0));
        mZombie.setFlipX(true);//水平翻转
        mZombie.setScale(0.65f);
        //设置僵尸的锚点在两腿之间
        mZombie.setAnchorPoint(0.5f, 0);

    }

    /**
     * 解析地图上的对象图层上的点
     */
    private void loadRoadPoint() {

        //创建一个点的集合
        mPoints = new ArrayList<>();
        CCTMXObjectGroup road = mMap.objectGroupNamed("road");
        ArrayList<HashMap<String, String>> objects = road.objects;

        for (HashMap<String, String> hashMap : objects) {
            String x = hashMap.get("x");
            String y = hashMap.get("y");
            float x_ = Float.parseFloat(x);
            float y_ = Float.parseFloat(y);

            CGPoint point = ccp(x_, y_);
            //添加点到集合
            mPoints.add(point);
        }

    }


    public void loadMap() {
        //1 加载地图
        mMap = CCTMXTiledMap.tiledMap("map.tmx");
        //3 打印地图默认描点
        CGPoint anchorPoint = mMap.getAnchorPoint();
        System.out.println("anchorPoint:" + anchorPoint);
        //4 为了地图后面可以移动，需要把锚点设置(0.5,0.5),这个地方比较特别
        mMap.setAnchorPoint(0.5f, 0.5f);
        //5 设置地图的position，让它贴在坐标原点
        CGSize mapSize = mMap.getContentSize();
        mMap.setPosition(mapSize.width / 2, mapSize.height / 2);
        //2 添加map到cclayer
        this.addChild(mMap);
    }


}
